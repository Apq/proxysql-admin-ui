# ProxySQL 前后端连接链路监控设计

## 1. 背景

当前项目首页已经显示 ProxySQL 的客户端连接统计，例如 `Client_Connections_connected`，但无法回答下面这些运维问题：

- 当前有多少客户端连接到 ProxySQL？
- 这些连接来自哪些客户端地址和用户？
- ProxySQL 当前把请求路由到了哪些后端 MySQL？
- 每个后端实际建立了多少连接、使用了多少连接、空闲了多少连接？
- 哪些连接属于 Writer、Reader、Backup Writer 或其他 Hostgroup？
- 当前是否有后端连接错误、连接池耗尽或正在执行的慢请求？

本设计增加一个连接链路监控页面，观察以下完整链路：

```text
前端业务客户端
        <== MySQL 客户端连接 ==>
ProxySQL
        <== 后端连接池连接 ==>
后端 MySQL Server
```

## 2. 目标

- 在一个页面中查看前端连接、ProxySQL 当前会话和后端连接池状态。
- 按后端服务器和 Hostgroup 汇总连接使用情况。
- 支持从汇总数据展开到当前请求明细。
- 能看出“前端连接数”和“后端物理连接数”的区别。
- 使用只读统计查询，不修改 ProxySQL 配置、不终止连接、不影响业务流量。
- 与现有 Hostgroup 页面关联，显示后端所在分组及角色。

## 3. 非目标

- 本期不提供踢除客户端连接、关闭后端连接或 Kill Query 操作。
- 本期不把连接统计写入应用数据库。
- 本期不实现历史趋势存储。页面只显示当前快照；历史趋势可以作为后续 Prometheus、时序库或应用采集任务的扩展。
- 本期不把前端连接数和后端连接数强行建立一对一关系。

## 4. 指标定义

### 4.1 前端连接

前端连接是业务客户端与 ProxySQL 建立的连接，主要来自 `stats_mysql_global`：

| 指标 | 数据项 | 含义 |
| --- | --- | --- |
| 当前连接数 | `Client_Connections_connected` | 当前连接到 ProxySQL 的客户端连接数 |
| 累计创建数 | `Client_Connections_created` | ProxySQL 启动以来创建过的客户端连接数 |
| 累计中止数 | `Client_Connections_aborted` | 异常中断或失败的客户端连接数 |
| 当前活动连接 | 版本相关统计项 | 如目标版本提供，则显示当前正在处理请求的前端连接数 |

项目当前首页已经读取 `Client_Connections_connected`、`Client_Connections_created` 和 `Client_Connections_aborted`。新页面应复用现有 `GetMySqlGlobalStats()`，不重复建立一套全局统计读取逻辑。

### 4.2 后端连接池

后端连接是 ProxySQL 到 MySQL Server 的连接，主要来自 `stats_mysql_connection_pool`。这张表按后端节点和 Hostgroup 提供连接池快照。

页面至少显示以下字段，具体列名必须以项目支持的 ProxySQL 版本实际返回结果为准：

| 页面指标 | 典型字段 | 含义 |
| --- | --- | --- |
| 已使用连接 | `ConnUsed` | 当前被请求使用的后端连接数 |
| 空闲连接 | `ConnFree` | 已建立但当前空闲的后端连接数 |
| 成功连接 | `ConnOK` | 成功建立的后端连接累计计数 |
| 失败连接 | `ConnERR` | 建立后端连接失败的累计计数 |
| 后端查询数 | `Queries` | 经该后端执行的查询累计数 |
| 发送字节数 | `Bytes_data_sent` | 发往后端的数据量 |
| 接收字节数 | `Bytes_data_recv` | 从后端接收的数据量 |
| 后端延迟 | `Latency_ms` 或 `Latency_us` | Monitor 报告的后端当前 Ping 延迟；页面统一换算为毫秒 |

`ConnUsed + ConnFree` 表示当前已建立的后端连接池规模，`ConnUsed` 不是后端 MySQL 的全部连接数。后端还可能存在绕过 ProxySQL 的直连，因此页面必须标注“ProxySQL 连接池连接”。

### 4.3 当前请求和会话明细

`stats_mysql_processlist`（若目标版本提供）用于查看当前正在 ProxySQL 中处理的会话或请求。它适合显示前端和后端之间的关联：

| 页面字段 | 说明 |
| --- | --- |
| Session ID / Thread ID | ProxySQL 会话标识 |
| 前端用户 | 连接 ProxySQL 使用的用户 |
| 客户端地址和端口 | 业务客户端来源 |
| 前端连接状态 | 当前会话状态 |
| Hostgroup | 当前路由到的主机组 |
| 后端地址和端口 | 当前使用的 MySQL 后端 |
| 后端用户 | ProxySQL 连接后端使用的用户；如版本提供 |
| 执行时间 | 当前请求持续时间 |
| Command | 当前命令状态 |
| Digest | 查询摘要 |
| Digest Text | 脱敏后的摘要 SQL；按权限决定是否显示 |

连接池表回答“建立了多少后端连接”，Processlist 表回答“当前有哪些请求正在经过 ProxySQL”。二者不能互相替代。

## 5. 页面设计

### 5.1 页面入口

在 MySQL 菜单下增加：

```text
Backend Connections
```

建议路由：

```text
/mysql/backend-connections
```

建议组件路径：

```text
ProxysqlAdminUi.Web/Components/Pages/MySql/Connections/MySqlBackendConnectionsPage.razor
```

### 5.2 顶部链路总览

页面顶部使用三组摘要指标，直观表达链路方向：

```text
前端客户端              ProxySQL 当前会话              后端连接池
当前连接：N             当前请求：N                    已使用：N
累计创建：N             活跃请求：N                    空闲：N
累计中止：N             路由 Hostgroup：N              连接总量：N
```

同时显示采样时间、数据刷新状态和自动刷新开关：

```text
采样时间：2026-08-12 09:50:00
[刷新] [自动刷新: 关闭] [刷新间隔: 5 秒]
```

默认自动刷新关闭，避免多个用户同时打开页面时持续增加 ProxySQL Admin 查询压力。开启后允许 `5 秒、10 秒、30 秒、60 秒` 四档间隔。

### 5.3 后端连接池表

主表按后端节点展示：

| Hostgroup | 角色 | 后端地址 | 状态 | 当前使用 | 当前空闲 | 当前总连接 | 最大连接 | 连接失败 | 查询数 | 延迟 |
| ---: | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 10 | Writer | `db-primary:3306` | ONLINE | 12 | 8 | 20 | 100 | 0 | 125,000 | 1.2 ms |
| 30 | Reader | `db-replica:3306` | ONLINE | 5 | 15 | 20 | 100 | 2 | 98,000 | 2.4 ms |

计算字段：

```text
当前总连接 = ConnUsed + ConnFree
使用率 = ConnUsed / max(1, ConnUsed + ConnFree)
剩余配置容量 = max_connections - (ConnUsed + ConnFree)
```

当 `ConnERR` 增长、使用率接近 100%、后端状态不是 `ONLINE` 或超过 `max_connections` 时，显示警告状态。

### 5.4 Hostgroup 汇总

在后端连接池表上方增加按 Hostgroup 汇总的切换：

```text
[按后端节点] [按 Hostgroup]
```

Hostgroup 汇总至少显示：

- Hostgroup ID
- Hostgroup 角色：Writer、Reader、Backup Writer、Offline、Undefined
- 后端节点数
- 当前使用连接数
- 当前空闲连接数
- 当前连接总量
- 连接上限合计
- 连接错误累计数
- 查询数
- 平均或 P95 延迟（只有数据源支持时显示）

角色名称来自现有的 `mysql_replication_hostgroups`、`mysql_galera_hostgroups` 或其他已支持拓扑定义；Hostgroup ID 本身没有固定角色含义。

### 5.5 当前请求明细

增加独立页签或展开面板：

```text
[连接池汇总] [当前请求]
```

当前请求表读取 `stats_mysql_processlist`，支持按以下字段筛选：

- 客户端地址
- 前端用户
- Hostgroup
- 后端地址
- 执行时间范围
- Digest

默认只加载前 `200` 条，并按执行时间倒序。表格必须分页或限制返回条数，不能对生产环境的 Processlist 做无条件全量高频刷新。

## 6. 数据访问设计

### 6.1 新增模型

新增以下模型，字段以实际版本探测结果为准：

```text
Models/StatsMySqlConnectionPoolModel.cs
Models/StatsMySqlProcesslistModel.cs
```

建议连接池模型包含：

```csharp
public sealed class StatsMySqlConnectionPoolModel
{
    public int Hostgroup { get; set; }
    public string ServerHost { get; set; } = string.Empty;
    public int ServerPort { get; set; }
    public long ConnUsed { get; set; }
    public long ConnFree { get; set; }
    public long ConnOk { get; set; }
    public long ConnErr { get; set; }
    public long Queries { get; set; }
    public long BytesDataSent { get; set; }
    public long BytesDataRecv { get; set; }
    public long LatencyMs { get; set; }
}
```

连接池统计表的字段以目标 ProxySQL 版本为准。Repository 通过 `SELECT * FROM stats_mysql_connection_pool LIMIT 0` 读取结果集元数据：优先使用 `Latency_ms`；仅存在 `Latency_us` 时除以 1000 并转换为毫秒；两列都不存在时返回 0。ProxySQL Admin 接口不保证支持 MySQL 的 `SHOW COLUMNS` 语法，因此不使用该语法探测表结构。

### 6.2 Repository 方法

在 [ProxySqlRepository.cs](../ProxysqlAdminUi.Web/Repositories/ProxySqlRepository.cs) 中增加：

```csharp
Task<ConnectionTopologySnapshotViewModel> GetConnectionTopologySnapshot();

Task<IReadOnlyList<StatsMySqlConnectionPoolModel>>
    GetMySqlConnectionPool(string? hostgroup = null);

Task<IReadOnlyList<StatsMySqlProcesslistModel>>
    GetMySqlProcesslist(int limit = 200);
```

`GetConnectionTopologySnapshot` 一次采集同一时刻的：

1. `stats_mysql_global` 中的客户端连接指标。
2. `stats_mysql_connection_pool` 中的后端连接池指标。
3. `stats_mysql_processlist` 中的当前请求指标（可选，失败时不影响连接池汇总）。
4. `mysql_servers` 或 runtime 表中的后端状态和 `max_connections`。
5. Hostgroup 角色定义，用于把数字翻译为业务角色。

不要让 Razor 页面分别发起上述查询并自行合并，避免页面看到不同时间点的快照。

### 6.3 查询示例

客户端全局统计：

```sql
SELECT Variable_Name, Variable_Value
FROM stats_mysql_global
WHERE Variable_Name IN (
    'Client_Connections_connected',
    'Client_Connections_created',
    'Client_Connections_aborted'
);
```

后端连接池：

```sql
SELECT *
FROM stats_mysql_connection_pool
ORDER BY hostgroup, srv_host, srv_port;
```

当前请求：

```sql
SELECT *
FROM stats_mysql_processlist
ORDER BY time_ms DESC
LIMIT 200;
```

实际开发前必须在目标 ProxySQL 版本上确认字段名称和 `LIMIT` 支持情况。应用应采用固定列清单，避免 `SELECT *` 的列变化导致页面绑定失败。

## 7. 连接关系和解释规则

### 7.1 不做一对一连接数比较

必须在页面上明确说明：

```text
前端连接数 != 后端连接总数
```

原因包括：

- ProxySQL 可以复用后端连接。
- 一个前端连接可能在不同时间访问不同后端。
- 一个后端连接在空闲时仍保留在连接池中。
- 事务、粘性连接、连接复用配置会改变前后端关联方式。
- 多个前端连接可能共享后端连接池资源。

### 7.2 请求链路展示

对于 `stats_mysql_processlist` 中能拿到完整关联字段的记录，展示：

```text
客户端 10.0.0.15:52134
    -> ProxySQL Session 1234
    -> Hostgroup 30（Reader）
    -> db-replica-01:3306
```

如果某个字段在目标版本不可用，显示“未提供”，不能根据 Hostgroup 或地址猜测具体连接关系。

### 7.3 配置和生效状态

后端节点的 Hostgroup、状态和 `max_connections` 应优先使用 `runtime_mysql_servers`，因为它代表当前生效配置。页面可同时读取 `main.mysql_servers` 用于显示配置差异，但不能用 `main` 的状态替代运行时状态。

## 8. 刷新、性能和错误处理

### 8.1 刷新策略

- 手动刷新：立即采集一次完整快照。
- 自动刷新：默认关闭，最小间隔 5 秒。
- 页面离开或浏览器标签不可见时暂停自动刷新。
- 上一次刷新未完成时不启动下一次刷新。
- 显示“数据采集时间”和“耗时”。

### 8.2 查询保护

- 当前请求默认最多返回 200 条。
- 后端连接池通常按后端节点返回，不应做无上限的明细扩展。
- 连接池统计和 Processlist 使用短生命周期 DbContext。
- 只读账号可以访问统计页，但如果缺少配置表权限，Hostgroup 角色显示为数字或“未提供”。
- 连接明细中的 SQL 摘要默认脱敏，避免在日志和 UI 中泄露敏感参数。

### 8.3 错误状态

页面需要区分：

- ProxySQL 连接失败：整页显示连接失败。
- `stats_mysql_connection_pool` 不存在或权限不足：显示“后端连接池统计不可用”。
- `stats_mysql_processlist` 不存在或权限不足：连接池汇总仍可显示，当前请求页显示不可用。
- 某些列不存在：保留其他字段，缺失列显示“未提供”。
- 查询超时：保留上一次成功快照，并标注快照已过期。

## 9. 分阶段实施

### 第一阶段：连接链路总览

- 增加后端连接池模型和 Repository 查询。
- 增加 `/mysql/backend-connections` 页面。
- 显示前端连接摘要和后端节点连接池表。
- 复用 runtime 服务器状态和现有 Hostgroup 角色显示。
- 提供手动刷新。

### 第二阶段：当前请求明细

- 增加 `stats_mysql_processlist` 模型和查询。
- 增加当前请求页签。
- 支持客户端、用户、Hostgroup、后端地址和执行时长筛选。
- 增加条数上限和自动刷新。

### 第三阶段：趋势和告警基础

- 以固定采样间隔将快照写入独立的应用存储或外部监控系统。
- 展示连接使用率、连接错误、连接池耗尽和前后端不匹配趋势。
- 通过现有调度或外部监控系统配置告警，不在页面刷新请求中执行长时间任务。

## 10. 测试和验收标准

- 页面能同时显示前端连接摘要、后端连接池汇总和当前请求明细。
- 后端连接池数据来自 `stats_mysql_connection_pool`，不是 `mysql_servers` 配置表。
- 前端连接数据来自 `stats_mysql_global`，并沿用已有首页统计定义。
- 当前请求数据来自 `stats_mysql_processlist`；该表不可用时不影响连接池汇总。
- 页面能够按 Hostgroup 显示 Writer、Reader、Backup Writer、Offline 或 Undefined 角色。
- 页面不会把前端连接数与后端连接数进行错误的一对一比较。
- 页面能显示每个后端的 `ConnUsed`、`ConnFree`、连接总量、连接错误和容量使用率。
- 自动刷新默认关闭，最小间隔为 5 秒，且不会并发发起多个刷新请求。
- 数据查询失败时保留上一次成功快照并显示过期状态。
- 不提供 Kill Connection、Kill Query 或修改后端状态等写操作。
- 不在页面、日志或异常信息中输出明文密码和未脱敏的敏感 SQL 参数。

## 12. 参考资料

- [ProxySQL Admin Tables](https://github.com/sysown/proxysql/blob/master/doc/admin_tables.md)
- [ProxySQL Main/Runtime Configuration](https://proxysql.com/documentation/main-runtime/)

## 11. 结论

这个功能最合适的实现方式不是增加一张“连接配置表”，而是增加一个只读的运行监控页面，按三层数据解释完整链路：

```text
stats_mysql_global
    -> 前端客户端连接到 ProxySQL 的总览

stats_mysql_processlist
    -> 当前经过 ProxySQL 的会话和请求链路

stats_mysql_connection_pool
    -> ProxySQL 到后端 MySQL 的物理连接池

runtime_mysql_servers + Hostgroup 定义表
    -> 后端当前生效状态和 Writer/Reader 角色
```

这样用户可以分别回答“有多少客户端连接”“当前请求去了哪里”和“ProxySQL 到每个后端实际保持了多少连接”，同时不会把不同语义的指标混在一起。
