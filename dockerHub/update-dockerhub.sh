#!/usr/bin/env bash
# Update Docker Hub repository metadata from the files in this directory.
# Required environment variables: DOCKERHUB_USERNAME, DOCKERHUB_TOKEN

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DH_NAMESPACE="${DOCKERHUB_NAMESPACE:-amwpfiqvy}"
DH_REPO="${DOCKERHUB_REPOSITORY:-proxysql-admin-ui}"
DH_USERNAME="${DOCKERHUB_USERNAME:-${DH_NAMESPACE}}"
DH_TOKEN="${DOCKERHUB_TOKEN:-}"
BASE_URL="https://hub.docker.com/v2/repositories/${DH_NAMESPACE}/${DH_REPO}"
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

if [[ -z "$DH_TOKEN" ]]; then
  echo "ERROR: DOCKERHUB_TOKEN is not set" >&2
  exit 1
fi

for command in curl jq; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "ERROR: required command not found: $command" >&2
    exit 1
  fi
done

description="$(tr -d '\r\n' < "$SCRIPT_DIR/description.md")"
category="$(tr -d '\r\n' < "$SCRIPT_DIR/category.txt" | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')"

description_bytes="$(printf '%s' "$description" | wc -c | tr -d ' ')"
if [[ -z "$description" || "$description_bytes" -gt 100 ]]; then
  echo "ERROR: description.md must contain 1-100 UTF-8 bytes (actual: ${description_bytes})" >&2
  exit 1
fi
if [[ -z "$category" ]]; then
  echo "ERROR: category.txt is empty" >&2
  exit 1
fi

login_response="$TMP_DIR/login.json"
curl -fsS \
  -H 'Content-Type: application/json' \
  -d "$(jq -n --arg username "$DH_USERNAME" --arg password "$DH_TOKEN" '{username: $username, password: $password}')" \
  'https://hub.docker.com/v2/users/login/' > "$login_response"
jwt="$(jq -r '.token // empty' "$login_response")"

if [[ -z "$jwt" ]]; then
  echo "ERROR: Docker Hub login did not return a JWT" >&2
  cat "$login_response" >&2
  exit 1
fi

overview_payload="$(jq -n \
  --arg description "$description" \
  --rawfile full_description "$SCRIPT_DIR/overview.md" \
  '{description: $description, full_description: $full_description}')"
http_code="$(curl -sS -o "$TMP_DIR/repository.json" -w '%{http_code}' -X PATCH "$BASE_URL/" \
  -H "Authorization: JWT $jwt" \
  -H 'Content-Type: application/json' \
  -d "$overview_payload")"
if [[ "$http_code" != "200" ]]; then
  echo "ERROR: Docker Hub description/overview update returned HTTP ${http_code}" >&2
  cat "$TMP_DIR/repository.json" >&2
  exit 1
fi

echo "Docker Hub description and overview updated for ${DH_NAMESPACE}/${DH_REPO}"

categories_response="$TMP_DIR/categories.json"
curl -fsS -H "Authorization: JWT $jwt" \
  'https://hub.docker.com/v2/categories/' > "$categories_response"

category_payload="$(jq -c --arg category "$category" '
  def norm: ascii_downcase | gsub("[^a-z0-9]+"; "-") | gsub("^-|-$"; "");
  (if type == "array" then . elif .results then .results else [] end)
  | map(select((.name == $category) or (.slug == $category) or ((.name | norm) == ($category | norm)) or ((.slug | norm) == ($category | norm))))
  | if length > 0 then [.[0] | {slug, name}] else empty end
' "$categories_response")"

if [[ -z "$category_payload" ]]; then
  echo "ERROR: Docker Hub category not found: ${category}" >&2
  exit 1
fi

http_code="$(curl -sS -o "$TMP_DIR/category.json" -w '%{http_code}' -X PATCH "${BASE_URL}/categories/" \
  -H "Authorization: JWT $jwt" \
  -H 'Content-Type: application/json' \
  -d "$category_payload")"
if [[ "$http_code" != "200" ]]; then
  echo "ERROR: Docker Hub category update returned HTTP ${http_code}" >&2
  cat "$TMP_DIR/category.json" >&2
  exit 1
fi

echo "Docker Hub category updated to ${category}"
