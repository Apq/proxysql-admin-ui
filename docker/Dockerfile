# syntax=docker/dockerfile:1

ARG BUILD_CONFIGURATION=Release

# Build stage — runs on native arch via buildx, cross-compiles to TARGETARCH
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
ARG BUILD_CONFIGURATION
ARG TARGETARCH
ARG APP_VERSION=0.0.0

WORKDIR /src

# Map TARGETARCH (amd64/arm64) to .NET RID for Alpine musl
RUN case "$TARGETARCH" in \
      amd64) echo 'linux-musl-x64' > /tmp/rid ;; \
      arm64) echo 'linux-musl-arm64' > /tmp/rid ;; \
      *) echo "Unsupported arch: $TARGETARCH" >&2; exit 1 ;; \
    esac

COPY ["ProxysqlAdminUi.Web/ProxysqlAdminUi.Web.csproj", "ProxysqlAdminUi.Web/"]
RUN dotnet restore --runtime "$(cat /tmp/rid)" "./ProxysqlAdminUi.Web/ProxysqlAdminUi.Web.csproj"

COPY . .
WORKDIR /src/ProxysqlAdminUi.Web

RUN dotnet build "./ProxysqlAdminUi.Web.csproj" \
      -c "$BUILD_CONFIGURATION" \
      --runtime "$(cat /tmp/rid)" \
      -o /app/build

RUN dotnet publish "./ProxysqlAdminUi.Web.csproj" \
      --runtime "$(cat /tmp/rid)" \
      --no-restore \
      -c "$BUILD_CONFIGURATION" \
      -o /app/publish \
      /p:UseAppHost=true \
      /p:Version="$APP_VERSION"

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine

# Enable globalization and time zones
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
  LC_ALL=en_US.UTF-8 \
  LANG=en_US.UTF-8
RUN apk add --no-cache icu-data-full icu-libs

EXPOSE 8001

WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["./ProxysqlAdminUi.Web"]
