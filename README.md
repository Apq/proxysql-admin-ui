![Docker Image Version](https://img.shields.io/docker/v/amwpfiqvy/proxysql-admin-ui?sort=semver)
![Docker Pulls](https://img.shields.io/docker/pulls/amwpfiqvy/proxysql-admin-ui)
![Docker Image Size](https://img.shields.io/docker/image-size/amwpfiqvy/proxysql-admin-ui/latest)
![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/Apq/proxysql-admin-ui/docker-publish.yml?branch=apqmain)

----

# ProxySQL Admin UI

A modern web interface for managing ProxySQL, built with Blazor and .NET Core.

## Screenshots

<a href="https://img.dotfinity.eu/proxysql-admin-ui-dashboard-page.png">
  <img src="https://img.dotfinity.eu/proxysql-admin-ui-dashboard-page.png" alt="Dashboard" width="400" height="200"/>
</a>
<a href="https://img.dotfinity.eu/proxysql-admin-ui-query-digest-stats-page.png">
  <img src="https://img.dotfinity.eu/proxysql-admin-ui-query-digest-stats-page.png" alt="Qiery digest stats" width="400" height="200"/>
</a>
<a href="https://img.dotfinity.eu/proxysql-admin-ui-query-rules-page.png">
  <img src="https://img.dotfinity.eu/proxysql-admin-ui-query-rules-page.png" alt="Query rules page" width="400" height="200"/>
</a>
<a href="https://img.dotfinity.eu/proxysql-admin-ui-global-variables-page.png">
  <img src="https://img.dotfinity.eu/proxysql-admin-ui-global-variables-page.png" alt="Admin global variables editor page" width="400" height="200"/>
</a>

## Features

- Dashboard with metrics
- ProxySQL management:
  - MySQL backend server add, edit, and delete operations
  - MySQL user add, edit, and delete operations
  - Automatic `LOAD ... TO RUNTIME` and `SAVE ... TO DISK` after server and user changes
  - Query rules configuration
  - Query digest grid with stats and filters
  - Global variables configuration

> [!CAUTION]
> This application can change live ProxySQL routing and authentication. Restrict network access, use a dedicated ProxySQL admin account, and verify backups and rollback procedures before using write operations in production.

## Tech Stack

- ASP.NET Core (.NET 8)  
- Blazor Server
- Entity Framework Core
- [Radzen Blazor Components](https://blazor.radzen.com/)
- SQLite (for auth data)

## Getting Started

### Prerequisites

- .NET 8 SDK
- Running ProxySQL instance

### Configuration

#### Local Configuration

1. Configure ProxySQL connection in `appsettings.json`:

```json
  "ConnectionStrings": {
    "ProxySqlContext": "Server=0.0.0.0;Port=6032;Uid=radmin;Pwd=radmin;ConnectionReset=False;Pooling=True;ConnectionLifeTime=3000000;"
  },
  "DefaultUsers": [
    {
      "Username": "admin"
    }
  ]
```

When the identity database has no users, the application generates a random 14-character initial password using only letters, digits, `_`, and `%`. The login page displays the initial credential until the user changes the password.

2. Start the application:

```bash
dotnet watch --project ProxysqlAdminUi.Web/ProxysqlAdminUi.Web.csproj
```

#### Windows Service Deployment

Windows in-place publishing and WinSW service management scripts are available in [`scripts/windows`](scripts/windows/就地部署.md). Published application files, identity data, logs, the downloaded WinSW executable, and the local service XML are kept outside version control. Repeated deployments replace only the application publish directory and preserve `data/db/app.db`.

#### Docker Configuration

Set the environment variables in the `docker-compose.yml` file:

```yml
environment:
  ASPNETCORE_ENVIRONMENT: Production
  ASPNETCORE_URLS: 'http://+:8001'
  # Connection string for the ProxySQL Admin server
  PAI_ConnectionStrings__ProxySqlContext: 'Server=xxxxxx;Port=6032;Uid=radmin;Pwd=radmin;ConnectionReset=False;Pooling=True;ConnectionLifeTime=3000000;'
```

or if you're running the app and the ProxySQL server in the same docker-compose file, you can use the service name as the host:

```yml
environment:
  ASPNETCORE_ENVIRONMENT: Production
  ASPNETCORE_URLS: 'http://+:8001'
  # Connection string for the ProxySQL Admin server
  PAI_ConnectionStrings__ProxySqlContext: 'Server=proxysql;Port=6032;Uid=radmin;Pwd=radmin;ConnectionReset=False;Pooling=True;ConnectionLifeTime=3000000;'
```

## Random notes

>One query rule can have many digests! 
> example: if one rule matches by table prefix "ps_setting*"
> if there are 10 tables being accessed, via 20 queries, that would result in 40 different digests. 
> 20 for the uncached queries and 20 for the cache hits. 


## License

[MIT](/LICENSE.md)

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
