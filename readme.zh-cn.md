# ProxySQL Admin UI

ProxySQL Admin UI 是一个基于 ASP.NET Core .NET 8 和 Blazor Server 的 ProxySQL 管理界面，用于查看和管理 ProxySQL 的后端服务器、用户、查询规则、查询摘要、全局变量和运行状态。

## 功能

- 查看 ProxySQL 仪表盘和运行统计
- 管理 `mysql_servers` 后端服务器
- 在 Replication/Galera 主机组页面查看并维护对应拓扑定义所引用的成员
- 管理 `mysql_users` 用户
- 管理 `mysql_query_rules` 查询路由和缓存规则
- 查看 `stats_mysql_query_digest` 查询摘要
- 查看和编辑全局变量
- 对服务器、用户和查询规则执行 `LOAD ... TO RUNTIME` 与 `SAVE ... TO DISK`
- 查看 ProxySQL Replication Hostgroup 的 Writer/Reader 定义及 Main、Runtime、Disk 成员关系
- 查看 ProxySQL Galera Hostgroup 的 Writer、Backup Writer、Reader、Offline 定义及三层成员关系

> [!CAUTION]
> 本应用可以修改 ProxySQL 的实时路由和认证配置。生产环境请限制 Web 界面访问来源，使用专用 ProxySQL Admin 账号，在变更前核对 `main`、`runtime` 和 `disk` 的差异，并准备回滚 SQL。

## 数据来源

应用通过 ProxySQL Admin 接口连接 ProxySQL，不连接后端 MySQL 业务库。配置数据和统计数据来自不同的表层：

| 层级 | 用途 | 示例 |
| --- | --- | --- |
| `main` | 配置编辑区 | `mysql_servers`、`mysql_users`、`mysql_query_rules` |
| `runtime` | 当前已加载到内存、正在生效的配置 | `runtime_mysql_servers`、`runtime_mysql_users`、`runtime_mysql_query_rules` |
| `disk` | 持久化配置，重启后用于恢复 | `disk.mysql_servers`、`disk.mysql_users`、`disk.mysql_query_rules` |
| `stats` | 查询、连接和全局运行统计 | `stats_mysql_query_digest`、`stats_mysql_global` |
| `monitor` | Monitor 模块产生的监控数据 | 监控检查日志和状态表 |

当前版本的配置页面默认查询 `main` 表。保存配置时先修改 `main`，再执行：

```sql
LOAD MYSQL SERVERS TO RUNTIME;
SAVE MYSQL SERVERS TO DISK;
```

用户和查询规则使用对应的 `LOAD MYSQL USERS/QUERY RULES TO RUNTIME` 与 `SAVE MYSQL USERS/QUERY RULES TO DISK` 命令。ProxySQL 实际处理流量使用 `runtime` 配置。

## 技术栈

- ASP.NET Core .NET 8
- Blazor Server
- Entity Framework Core
- MySql.EntityFrameworkCore
- Radzen Blazor Components
- SQLite（仅用于 Web 管理员身份认证）

## 快速开始

### 前置条件

- .NET 8 SDK
- 正在运行的 ProxySQL 实例
- 可访问 ProxySQL Admin 端口的账号

### 本地运行

通过必需的 `PAI_PROXYSQL` 环境变量配置 ProxySQL Admin 连接串：

```powershell
$env:PAI_PROXYSQL = 'Server=127.0.0.1;Port=6032;Uid=admin;Pwd=CHANGE_ME;ConnectionReset=False;Pooling=True;ConnectionLifeTime=3000000;'
# 可选：启用 Galera 仲裁权重读取和调整
$env:PAI_GALERA_USERNAME = 'galera_admin'
$env:PAI_GALERA_PASSWORD = 'CHANGE_ME'
```

启动应用：

```bash
dotnet watch --project ProxysqlAdminUi.Web/ProxysqlAdminUi.Web.csproj
```

默认监听地址为 `http://localhost:8001`。首次启动时，应用会在本地 SQLite 数据库中初始化固定用户名 `admin` 的 Web 管理员账号。

当身份数据库中没有用户时，应用会自动生成 14 位随机初始密码，字符范围为字母、数字、`_` 和 `%`。登录页面会显示初始账号和密码，用户修改密码成功后该提示自动移除。

### Docker 运行

在 Docker Compose 或容器环境中设置连接串：

```yaml
environment:
  ASPNETCORE_ENVIRONMENT: Production
  ASPNETCORE_URLS: http://+:8001
  PAI_PROXYSQL: Server=proxysql;Port=6032;Uid=admin;Pwd=CHANGE_ME;ConnectionReset=False;Pooling=True;ConnectionLifeTime=3000000;
```

项目提供了 [docker/docker-compose.yaml](docker/docker-compose.yaml) 和 [docker/docker-compose-build.yaml](docker/docker-compose-build.yaml) 示例。

### Windows 服务部署

Windows 就地发布和 WinSW 服务管理脚本位于 [scripts/windows](scripts/windows)。部署说明见 [scripts/windows/就地部署.md](scripts/windows/就地部署.md)。服务配置文件包含连接凭据，请勿提交或公开。

## 页面和路由

| 页面 | 路由 |
| --- | --- |
| 首页仪表盘 | `/` |
| MySQL 用户 | `/mysql/users` |
| MySQL 后端服务器 | `/mysql/servers` |
| MySQL Galera 主机组 | `/mysql/galera-hostgroups` |
| MySQL 复制主机组 | `/mysql/replication-hostgroups`（兼容旧地址 `/mysql/hostgroups`） |
| 查询规则 | `/mysql/rules` |
| 查询摘要 | `/mysql/queries/digest` |
| ProxySQL 全局变量 | `/proxysql/global-variables` |
| ProxySQL 操作 | `/proxysql/actions` |

## 配置变更原则

ProxySQL 的配置修改建议遵循以下顺序：

```text
读取 main
  -> 修改 main
  -> LOAD ... TO RUNTIME
  -> 验证 runtime
  -> SAVE ... TO DISK
  -> 验证 disk
```

`runtime` 和 `disk` 页面应作为只读对照视图。不要直接把用户输入拼接到 SQL 或表名中，层级和表名必须使用代码中的固定映射。

## 开发约定

- 页面组件位于 `ProxysqlAdminUi.Web/Components/Pages`
- ProxySQL 数据模型位于 `ProxysqlAdminUi.Web/Models`
- ProxySQL 数据访问集中在 `ProxysqlAdminUi.Web/Repositories/ProxySqlRepository.cs`
- ProxySQL EF Core 上下文位于 `ProxysqlAdminUi.Web/Contexts/ProxySqlContext.cs`
- 认证数据使用独立的 SQLite 上下文，不要与 ProxySQL 表混用

新增 ProxySQL 功能时，优先复用 Repository，避免在 Razor 页面中直接创建数据库连接或执行 SQL。

## 相关设计文档

- [ProxySQL 主机组定义显示设计](docs/mysql-replication-hostgroups-design.zh-CN.md)
- [ProxySQL Galera 主机组显示设计](docs/mysql-galera-hostgroups-design.zh-CN.md)
- [三类配置表支持方案](docs/three-table-support-plan.zh-CN.md)
- [ProxySQL 前后端连接链路监控设计](docs/backend-connection-topology-monitor-design.zh-CN.md)
- [ProxySQL 前后端连接链路监控实施步骤](docs/backend-connection-topology-monitor-implementation.zh-CN.md)

## 许可证

[MIT](LICENSE.md)

## 贡献

欢迎提交 Issue 和 Pull Request。涉及 ProxySQL 配置写入的变更应同时补充验证步骤和回滚说明。
