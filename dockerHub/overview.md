# ProxySQL Admin UI

ProxySQL Admin UI 是一个基于 Blazor Server 和 .NET 10 的 ProxySQL Web 管理界面。应用主要通过 ProxySQL Admin 接口读取配置和运行统计；启用 Galera 仲裁权重功能后，会使用独立凭据直连 ProxySQL Runtime Galera 拓扑中的节点。

当前版本：`v0.6`

## 功能

- 查看 ProxySQL 仪表盘、全局状态和查询摘要
- 管理 `mysql_servers`、`mysql_users` 和 `mysql_query_rules`
- 修改服务器、用户和查询规则后执行对应的 `LOAD ... TO RUNTIME` 与 `SAVE ... TO DISK`
- 分别查看 `main`、`runtime`、`disk` 三层配置；`runtime` 和 `disk` 为只读视图
- 独立查看 Replication Hostgroup 的 Writer/Reader 定义和成员关系
- 独立查看 Galera Hostgroup 的 Writer、Backup Writer、Reader、Offline 定义和成员关系
- 在 Galera Hostgroup 的 Runtime TAB 中读取和调整节点当前生效的 `pc.weight` 仲裁权重
- 在“连接总览”中查看前端连接统计和后端连接池状态
- 在“连接总览”中释放整个 ProxySQL 实例后端连接池中的空闲物理连接（`ConnFree`），不会中断正在使用的 `ConnUsed` 连接
- 在“连接详情”中查看前端客户端、ProxySQL 会话、Hostgroup 与后端 MySQL 的当前绑定关系
- 连接详情使用服务端分页，记录总数没有 200 条上限
- 提供中文和英文界面
- 提供 `/health` 健康检查端点

> [!CAUTION]
> 本应用可以修改线上 ProxySQL 的路由和认证配置，也可以修改 Galera 节点进程当前生效的仲裁权重。生产环境请限制 Web 界面访问来源，使用专用管理账号，并在执行写操作前确认集群状态和回滚方案。

## 快速开始

启动容器并配置 ProxySQL Admin 连接：

```bash
docker volume create proxysql-admin-ui-data

docker run -d --restart unless-stopped \
  --name proxysql-admin-ui \
  -p 8001:8001 \
  -e APP_DB_PATH=/app/data \
  -e PAI_PROXYSQL='Server=host.docker.internal;Port=6032;Uid=radmin;Pwd=CHANGE_ME;ConnectionReset=False;Pooling=True;ConnectionLifeTime=3000000;' \
  -v proxysql-admin-ui-data:/app/data \
  amwpfiqvy/proxysql-admin-ui:v0.6
```

启动后访问：`http://localhost:8001`

`host.docker.internal` 用于从容器访问 Docker 宿主机上的 ProxySQL。如果 ProxySQL 位于其他服务器，请将 `Server` 改为实际地址；如果两者位于同一个 Docker Compose 网络，可以使用 ProxySQL 服务名。

如需启用 Galera 仲裁权重功能，再增加以下两个环境变量：

```bash
-e PAI_GALERA_USERNAME='galera_admin' \
-e PAI_GALERA_PASSWORD='CHANGE_ME'
```

这两个变量只提供节点管理凭据。实际节点地址来自 ProxySQL Runtime Galera Hostgroup 成员，容器必须能够访问这些节点的 MySQL 端口。

## Docker Compose

```yaml
services:
  proxysql-admin-ui:
    image: amwpfiqvy/proxysql-admin-ui:v0.6
    container_name: proxysql-admin-ui
    restart: unless-stopped
    ports:
      - "8001:8001"
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ASPNETCORE_URLS: http://+:8001
      APP_DB_PATH: /app/data
      PAI_PROXYSQL: Server=proxysql;Port=6032;Uid=radmin;Pwd=CHANGE_ME;ConnectionReset=False;Pooling=True;ConnectionLifeTime=3000000;
      # 以下两项可选，必须同时配置
      PAI_GALERA_USERNAME: galera_admin
      PAI_GALERA_PASSWORD: CHANGE_ME
    volumes:
      - proxysql-admin-ui-data:/app/data

volumes:
  proxysql-admin-ui-data:
```

如果使用绑定挂载代替命名卷，请确保挂载目录对容器运行用户可写。

## 首次登录

身份数据库中没有用户时，应用会创建配置的 Web 管理员，并生成一个 14 位随机初始密码。密码字符范围为：

```text
A-Z  a-z  0-9  _  %
```

登录页会显示初始用户名和密码。管理员成功修改密码后，初始凭据提示会自动删除。

首次初始化的 Web 管理员用户名固定为 `admin`。应用不会从环境变量读取固定的 Web 初始密码。已有身份数据库不会在容器重启或升级时重置管理员密码。

## 数据持久化

当 `APP_DB_PATH=/app/data` 时，数据卷中包含：

- `db/app.db`：Web 管理员身份数据库
- `db/initial-login.json`：尚未修改密码时的临时初始凭据提示

升级或替换容器时应保留该数据卷。不要把 `initial-login.json` 上传、提交或公开；管理员修改密码后应用会自动删除它。

## 配置项

| 环境变量 | 必需 | 说明 |
| --- | --- | --- |
| `PAI_PROXYSQL` | 是 | ProxySQL Admin 连接字符串，通常使用 Admin 端口 `6032` |
| `PAI_GALERA_USERNAME` | 否 | Galera 节点管理用户名；与 `PAI_GALERA_PASSWORD` 同时配置后启用仲裁权重功能 |
| `PAI_GALERA_PASSWORD` | 否 | Galera 节点管理密码；与 `PAI_GALERA_USERNAME` 同时配置后启用仲裁权重功能 |
| `APP_DB_PATH` | 建议 | 身份数据根目录；容器部署建议设为 `/app/data` 并挂载持久化卷 |
| `ASPNETCORE_URLS` | 否 | Web 监听地址，镜像默认使用 `8001` 端口 |

环境变量中的双下划线 `__` 对应 .NET 配置中的层级分隔符。

## 页面说明

- Galera Hostgroup 的 Main TAB 管理 ProxySQL Main 配置；Runtime TAB 展示 ProxySQL 当前运行拓扑，并额外读取 Galera 节点进程当前生效的 `pc.weight`。
- “ProxySQL 路由权重”对应 `mysql_servers.weight`；“Galera 仲裁权重”对应节点 `wsrep_provider_options` 中的 `pc.weight`，两者不会互相替代。
- Galera 仲裁权重仅在 Runtime TAB 读取和修改。相同 `hostname:port` 出现在多个 Hostgroup 时，一次页面刷新只读取一次并共享显示结果。
- 修改仲裁权重前会重新校验 Runtime 成员、节点身份、集群 UUID、旧权重以及 `Primary / Synced / Ready / Connected` 状态；修改后再次读取并验证结果。
- 仲裁权重修改只影响当前 Galera 进程，不编辑节点配置文件，节点重启后可能丢失。
- Galera 节点连接使用 TLS 优先模式；节点不支持 TLS 时允许回退到非 TLS，生产环境建议在数据库侧正确配置 TLS。
- “连接总览”展示 ProxySQL 前端连接统计、后端物理连接总数、占用连接和空闲连接，以及各 Hostgroup 的连接池状态。
- “连接总览”支持释放整个 ProxySQL 实例的后端空闲物理连接（`ConnFree`）；Hostgroup 筛选只影响页面显示，不限制释放范围，正在使用的 `ConnUsed` 连接不会被中断。
- “连接详情”以 ProxySQL 前端会话为主行，展示客户端地址、Session ID、Thread ID、Hostgroup、后端地址、命令和 SQL。
- multiplex 生效时，空闲前端会话可能暂时显示为“未绑定”；连接池中的 `ConnFree` 也不会强行映射到某个前端会话。
- 连接详情采用 `COUNT(*)` 和 `LIMIT/OFFSET` 服务端分页，`10 / 20 / 50 / 100 / 200` 仅表示单页行数。

## 支持架构

| 架构 | 状态 |
| --- | --- |
| `linux/amd64` | 支持 |
| `linux/arm64` | 支持 |

## 健康检查

```text
GET http://localhost:8001/health
```

返回 HTTP `200` 表示 Web 应用健康检查通过。

## 相关链接

- 源码：https://github.com/Apq/proxysql-admin-ui
- 版本标签：https://github.com/Apq/proxysql-admin-ui/releases/tag/v0.6
- 许可证：MIT
