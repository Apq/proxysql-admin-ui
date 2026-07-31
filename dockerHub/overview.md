# ProxySQL Admin UI

A web interface for managing [ProxySQL](https://www.proxysql.com/) configuration, built with Blazor Server and .NET 8.

## Features

- Dashboard and ProxySQL statistics
- Add, edit, delete, and refresh `mysql_servers`
- Add, edit, delete, and refresh `mysql_users`
- Query rules configuration
- Query digest statistics and filters
- Global variables configuration
- Automatic `LOAD ... TO RUNTIME` and `SAVE ... TO DISK` after server and user changes
- Health endpoint at `/health`

> [!WARNING]
> This application can change live ProxySQL routing and authentication. Restrict access to the UI, use a dedicated ProxySQL Admin account, verify Runtime and Disk configuration before changes, and prepare rollback statements.

## Quick start

Run the image and point it at a ProxySQL Admin endpoint:

```bash
docker run -d --restart unless-stopped \
  --name proxysql-admin-ui \
  -p 8001:8001 \
  -e PAI_ConnectionStrings__ProxySqlContext='Server=host.docker.internal;Port=6032;Uid=radmin;Pwd=CHANGE_ME;ConnectionReset=False;Pooling=True;ConnectionLifeTime=3000000;' \
  -e PAI_DefaultUsers__0__Username=admin \
  -e PAI_DefaultUsers__0__Password=CHANGE_ME_TOO \
  amwpfiqvy/proxysql-admin-ui:latest
```

Open `http://localhost:8001` after the container starts.

For a Docker Compose setup with ProxySQL on the same network:

```yaml
services:
  proxysql-admin-ui:
    image: amwpfiqvy/proxysql-admin-ui:latest
    restart: unless-stopped
    ports:
      - "8001:8001"
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ASPNETCORE_URLS: http://+:8001
      PAI_ConnectionStrings__ProxySqlContext: Server=proxysql;Port=6032;Uid=radmin;Pwd=CHANGE_ME;ConnectionReset=False;Pooling=True;ConnectionLifeTime=3000000;
      PAI_DefaultUsers__0__Username: admin
      PAI_DefaultUsers__0__Password: CHANGE_ME_TOO
```

## Configuration

| Variable | Description |
|---|---|
| `PAI_ConnectionStrings__ProxySqlContext` | ProxySQL Admin connection string |
| `PAI_DefaultUsers__0__Username` | Initial Web UI administrator username |
| `PAI_DefaultUsers__0__Password` | Initial Web UI administrator password |
| `ASPNETCORE_URLS` | Listening URL; the image defaults to port `8001` |

The Web UI identity database is stored in the application data directory. Mount a persistent volume at `/app/data` if you need to preserve users across container replacement.

## Supported architectures

| Architecture | Status |
|---|---|
| `linux/amd64` | Supported |
| `linux/arm64` | Supported |

## Links

- Source: https://github.com/Apq/proxysql-admin-ui
- License: MIT
