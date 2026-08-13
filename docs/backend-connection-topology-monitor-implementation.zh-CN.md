# 前后端连接链路监控实施步骤

本文档用于跟踪 `backend-connection-topology-monitor-design.zh-CN.md` 的代码落地进度。

## 实施清单

- [x] 1. 明确实施范围和数据来源：`stats_mysql_global`、`stats_mysql_connection_pool`、`stats_mysql_processlist`、Runtime 后端配置和 Hostgroup 角色。
- [x] 2. 增加连接池、Processlist 和连接链路快照数据模型。
- [x] 3. 在 `ProxySqlContext` 注册统计模型。
- [x] 4. 在 `ProxySqlRepository` 增加前端连接、后端连接池、当前请求和 Hostgroup 过滤查询。
- [x] 5. 新增 Backend Connections 页面，显示链路总览、后端连接池和当前请求。
- [x] 6. 增加菜单入口和中英文文案。
- [x] 7. 增加 Hostgroup 页面到连接监控页面的筛选跳转。
- [x] 8. 完成静态核对、文档链接核对和变更摘要。
- [x] 9. 增加自动刷新、用户自定义间隔和浏览器本地持久化。
- [x] 10. 区分前端与后端连接的活跃/占用和空闲数，并持久化当前请求表的每页行数。
- [x] 11. 将逐会话连接关系拆分为独立页面，并改为服务端总数查询、分页和排序。

## 本次实现边界

- 页面默认每 30 秒自动刷新；用户可关闭自动刷新或设置 5 至 3600 秒的间隔，选择保存在浏览器 Local Storage 中。刷新任务串行执行，不会在前一次采集未完成时重叠查询 ProxySQL Admin。
- 前端连接使用 `Client_Connections_connected` 作为总数、`Client_Connections_non_idle` 作为活跃数，两者之差作为空闲数；后端连接使用 `ConnUsed + ConnFree` 作为总数，并分别展示占用和空闲数。
- 独立连接情况页面路由为 `/mysql/connection-sessions`，每行显示“前端客户端 -> ProxySQL 会话 -> Hostgroup -> 后端 MySQL”的当前关系。
- 连接情况页面先执行 `COUNT(*)` 获取完整总数，再通过 `LIMIT/OFFSET` 仅读取当前页；总记录数不设 200 条上限。每页行数支持 `10/20/50/100/200`，默认 20，并保存到浏览器 Local Storage。
- `200` 仅表示单页最大读取数量，不是全部连接的返回上限；用户可以通过分页访问完整的 `stats_mysql_processlist`。
- 页面只读，不提供 Kill Connection、Kill Query、修改后端状态或修改 Hostgroup 的操作。
- 后端连接池使用 ProxySQL 统计字段：`hostgroup`、`srv_host`、`srv_port`、`status`、`ConnUsed`、`ConnFree`、`ConnOK`、`ConnERR`、`Queries`、`Bytes_data_sent`、`Bytes_data_recv`；延迟字段兼容 `Latency_ms` 和 `Latency_us`，页面统一显示毫秒。
- 逐会话关系使用 `stats_mysql_processlist` 的固定字段，并对支持的排序字段使用服务端白名单映射。
- 对当前已绑定后端的前端会话，页面显示对应的后端地址；multiplex 已释放后端的空闲前端会话显示“未绑定”。`ConnFree` 代表后端连接池中的空闲物理连接，不强行映射到某个前端会话。

## 进度记录

| 项目 | 状态 | 说明 |
| --- | --- | --- |
| 设计和实施范围 | 已完成 | 已明确三类统计来源和只读边界 |
| 数据访问 | 已完成 | 已新增固定列查询、快照聚合和 Hostgroup 过滤 |
| 页面和菜单 | 已完成 | 已新增 `/mysql/backend-connections` 和中文文案 |
| 逐会话连接关系 | 已完成 | 已新增 `/mysql/connection-sessions`，支持无总行数上限的服务端分页 |
| Hostgroup 联动 | 已完成 | Runtime 成员可带 Hostgroup ID 跳转到连接池 |
| 静态核对 | 已完成 | 已检查路由、文档链接、本地化键、尾随空格和 Git diff；未启动应用或连接 ProxySQL |
