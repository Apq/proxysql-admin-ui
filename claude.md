# ProxySQL Admin UI - AI 助手上下文

## 项目概览

**ProxySQL Admin UI**：管理 ProxySQL（高性能 MySQL 代理）的现代 Web 管理面板。.NET 10 + Blazor Server，提供监控性能、配置后端服务器、管理用户凭据、定义查询路由规则的界面。

- **许可**：MIT
- **语言**：C#（.NET 10）
- **UI**：Blazor Server + Radzen 组件
- **主项目**：`ProxysqlAdminUi.Web/ProxysqlAdminUi.Web.csproj`
- **入口**：`Program.cs`，ASP.NET Core 端口 8000（可配）
- **部署**：Docker（Alpine）
- **数据库**：ProxySQL（MySQL 协议 6033）+ SQLite（本地认证）

## 技术栈

- .NET 10（Alpine 运行时）、ASP.NET Core、Blazor Server（SignalR 实时 UI）
- EF Core 8.0.10；Radzen Blazor（Material Design）+ Material 图标
- 数据访问：`MySql.EntityFrameworkCore`（ProxySQL）、`Microsoft.EntityFrameworkCore.Sqlite`（认证库）、复杂 ProxySQL 操作用直接 SQL
- 辅助：Newtonsoft.Json 13.0.3、Swashbuckle 6.9.0（OpenAPI）、ASP.NET Core Identity（Cookie 认证）
- 测试：xUnit 2.9.0、Aspire.Hosting.Testing（集成）、coverlet

## 架构（分层）

UI 层（`Pages/*.razor`、`Layout/MainLayout.razor`、`CustomComponents/`）→ 服务层（`DefaultUserSeedService.cs`、组件内业务逻辑）→ 仓储层（`Repositories/ProxySqlRepository.cs`）→ 数据层（`Contexts/ProxySqlContext.cs`、`ProxysqlAdminUiWebAuthContext.cs`）→ 数据库（ProxySQL + SQLite）。

关键模式：仓储模式抽象数据访问；不同数据源分 DbContext；DI 在 `Program.cs`；Blazor 组件 code-behind（`@code`）。

## 目录速览

```
ProxysqlAdminUi.Web/
├── Components/
│   ├── Account/              # 认证 UI（登录/注册）
│   ├── Pages/                # 页面：Home.razor（指标面板）、MySql/（Servers、Users、Rules、Queries）、ProxySQL/
│   ├── CustomComponents/     # 复用组件（如 TimeInput）
│   └── Layout/               # MainLayout、NavMenu
├── Contexts/                 # 两个 EF Core DbContext
├── Data/                     # Identity 与数据工具
├── Models/                   # 实体模型（映射数据库表）
├── Repositories/ProxySqlRepository.cs
├── Services/DefaultUserSeedService.cs
├── Extensions/FormatHelper.cs
├── ViewModel/  Migrations/  wwwroot/
├── Program.cs  App.razor  Routes.razor  _Imports.razor  appsettings.json
```

## 关键文件

- **`Program.cs`**：服务注册（Blazor/Radzen/Identity/DbContexts/Repository）、Cookie 认证、库初始化迁移、Swagger。
- **`appsettings.json`**：ProxySQL 连接串、默认管理员凭据、Kestrel、日志。
- **`Contexts/ProxySqlContext.cs`**：映射 ProxySQL 表（mysql_servers、mysql_users、mysql_query_rules、stats_*）。
- **`Repositories/ProxySqlRepository.cs`**：服务器/用户/规则 CRUD、统计查询、复杂查询直跑 SQL；方法如 `GetServers()`、`GetUsers()`、`GetQueryRules()`、`GetQueryDigest()`。
- **模型**：`MysqlServerModel`、`MysqlUserModel`、`MysqlQueryRuleModel`（38 属性）、`GlobalVariableModel`、统计类（`StatsMySqlGlobalModel`、`StatsMysqlQueryDigestModel`、`StatsMemoryMetricsModel`）。
- **`Components/Pages/Home.razor`**（252 行）：8 张指标卡（缓存效率/内存/健康/服务器/连接/流量/在线时长），Radzen 仪表盘。
- **`Layout/MainLayout.razor`**：顶栏 6 大区（Home、Users、Servers、Rules、Queries、Variables）+ 版本徽章页脚。
- **`Extensions/FormatHelper.cs`**：`FormatBytes()`、`FormatUptime()`、`FormatLargeNumber()`、`FormatMicroseconds()`。

## 数据库

**ProxySQL**（MySQL 协议）：连接在 `appsettings.json`（host localhost / Docker 内 `proxysql`，端口 6033，radmin/radmin）。表：`mysql_servers`、`mysql_users`、`mysql_query_rules`、`stats_mysql_query_digest`、`stats_mysql_global`、`stats_memory_metrics`、`global_variables`。

**Identity（SQLite）**：`/app/db/app.db`（Docker）或 `./db/app.db`（本地）；ASP.NET Core Identity 表；迁移在 `Migrations/AuthMigrations/`。

## 开发流程

```bash
# 本地热重载（http://localhost:5203）
dotnet watch --project ProxysqlAdminUi.Web/ProxysqlAdminUi.Web.csproj
dotnet build
dotnet test

# Docker 全栈（app:8000 / ProxySQL 管理:6080 / MariaDB:3306）
cd docker && docker-compose up -d

# Docker 构建
docker build -f docker/Dockerfile -t proxysql-admin-ui .

# Identity 迁移
dotnet ef migrations add MigrationName --context ProxysqlAdminUiWebAuthContext --project ProxysqlAdminUi.Web
```

- **新页面**：`Components/Pages/` 建 `.razor` → 顶部 `@page "/route"` → 受保护页加 `@attribute [Authorize]` → `MainLayout.razor` 加导航 → 用 Radzen 组件保持一致。
- **数据库变更**：改 `Models/` 实体 → 必要时更新 `ProxySqlContext.cs` → 更新仓储方法。
- **新 ProxySQL 功能五步**：建模型 → Context 加 `DbSet<T>` → 仓储实现 CRUD → 建页面组件 → 加菜单项。
- **加统计指标**：建 `Stats*Model.cs` → 仓储加查询方法 → 更新 `Home.razor` 或建专页 → Radzen 图表可视化。
- **改认证**：`Data/ProxysqlAdminUiWebUser.cs`、`Contexts/ProxysqlAdminUiWebAuthContext.cs`、`Components/Account/`、`Program.cs` 的 Identity 注册。

## 构建与部署

- **Dockerfile**：`docker/Dockerfile` 多阶段（SDK 构建→Alpine 运行时，约 350MB），`--build-arg BUILD_VERSION/BUILD_SUFFIX`。
- **CI/CD**：`dev` 分支 → `docker-container-builder.yml` → 私有库 proget.dotfinity.eu；`main` 分支 → `docker-container-publish.yml` → Docker Hub（dotfinity/proxysql-admin-ui），并打 git tag。标签格式 `{DATE}-{RUN_ID}`、`latest`。
- **分支模型**：main=生产发布，dev=开发构建，功能分支走标准 GitHub flow。

## 环境配置

- `ASPNETCORE_ENVIRONMENT`、`ASPNETCORE_URLS`（默认 http://+:8000）
- `PAI_PROXYSQL`（连接串）、`APP_DB_PATH`（SQLite 位置，默认 /app/db/）
- `BUILD_VERSION`（YYYY.MM.DD）、`BUILD_SUFFIX`（GitHub run ID）；页脚徽章链到 GitHub release
- 配置文件：`appsettings.json`、`appsettings.Development.json`、`docker/proxysql.cnf`、`docker/docker-compose.yaml`

## 测试

- 项目：`ProxysqlAdminUi.Tests/`，xUnit + Aspire.Hosting.Testing 集成测试，`dotnet test`。
- 现有：`WebTests.cs` 验证应用启动返回 200。
- 新测试用 `DistributedApplicationTestingBuilder.CreateAsync<Projects.ProxysqlAdminUi_Web>()` 模式。

## 约定

- **命名**：模型 `{Entity}Model.cs`、页面 `{Feature}Page.razor`/`{Entity}.razor`、VM `{Feature}ViewModel.cs`、服务 `{Purpose}Service.cs`。
- **代码风格**：C# 文件作用域命名空间、构造器注入、数据库操作 async/await、`@code` 块。
- **数据库操作**：走 `ProxySqlRepository` 方法（不绕过仓储）、全 async、DbContext 交 DI 释放；ProxySQL 表改后需 `LOAD ... TO RUNTIME` / `SAVE ... TO DISK`。

## ProxySQL 专有要点

- **管理动作**：改配置（服务器/用户/规则）后执行 `LOAD {TABLE} TO RUNTIME` 应用、`SAVE {TABLE} TO DISK` 持久化。表如 `MYSQL SERVERS`、`MYSQL USERS`、`MYSQL QUERY RULES`、`MYSQL VARIABLES`。
- **查询规则**：按 `rule_id` 顺序求值；digest=规范化查询的 MD5；命中 flagOUT 规则停止后续求值；`cache_ttl` 列设缓存毫秒数。
- **连接流**：Client → ProxySQL(6033) → 查询规则（路由/缓存）→ 主机组 → 后端 MySQL。

## 排障

- **连不上 ProxySQL**：查 `appsettings.json` 连接串、6033 是否在听、radmin/radmin。
- **SQLite 锁**：查 `APP_DB_PATH` 权限、`/app/db/` 存在可写、Docker 卷挂载。
- **改动不生效**：配置后没 `LOAD ... TO RUNTIME`；用 Actions 页或直跑 SQL。
- **认证不工作**：SQLite 库存在且迁移已应用、`Program.cs` 默认用户创建、`DefaultUserSeedService.cs` 日志。

## 依赖与资源

- 关键 NuGet：`Radzen.Blazor`、`MySql.EntityFrameworkCore`、`Microsoft.EntityFrameworkCore.Sqlite`、`Microsoft.AspNetCore.Identity.EntityFrameworkCore`。
- Dependabot：`.github/dependabot.yml`，周更，目标是 Web 项目的 NuGet。
- 文档：[ProxySQL](https://proxysql.com/documentation/)、[.NET](https://learn.microsoft.com/en-us/dotnet/)、[Blazor](https://learn.microsoft.com/en-us/aspnet/core/blazor)、[Radzen](https://blazor.radzen.com/)。
- 版本：.NET 10.0（LTS）、EF Core 8.0.10、Radzen（最新）、ProxySQL 2.7.1、MariaDB 11（后两者 Docker）。

---

**最后更新**：2025-11-04（AI 辅助生成）。本文档帮助 AI 助手理解代码库结构、约定与开发流程。
