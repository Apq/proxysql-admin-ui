# Galera 节点仲裁权重管理设计

本文档描述 ProxySQL Admin UI 当前已经实现的 Galera 节点仲裁权重管理功能。内容以仓库现有代码为准，不包含尚未实现的规划。

## 1. 功能定位

系统在 Galera Hostgroups 页面的 `Runtime` TAB 中读取和修改 Galera 节点当前进程内生效的 `pc.weight`。

页面地址：

```text
/mysql/galera-hostgroups#runtime
```

这里同时存在两种含义完全不同的权重：

| 名称 | 数据来源 | 作用 |
| --- | --- | --- |
| ProxySQL 路由权重 | `runtime_mysql_servers.weight` | 影响 ProxySQL 的后端选路和流量分配 |
| Galera 仲裁权重 | Galera 节点 `wsrep_provider_options` 中的 `pc.weight` | 影响 Galera Primary Component 的加权仲裁 |

“Galera 仲裁权重”不是 ProxySQL 配置字段。系统不会因为修改 `pc.weight` 而修改 ProxySQL Main、Runtime 或 Disk 表中的 `mysql_servers.weight`。

## 2. 当前范围

当前实现包括：

- 仅在 `Runtime` TAB 展示“Galera 仲裁权重”列。
- 从 ProxySQL Runtime Galera 拓扑取得需要访问的节点端点。
- 直连 Galera 节点读取当前运行中的 `pc.weight` 和 WSREP 状态。
- 在高风险操作对话框中修改单个节点的运行时 `pc.weight`。
- 修改前重新验证 Runtime 成员身份、节点身份、集群身份、旧权重和节点健康状态。
- 修改后重新读取节点并验证新权重、集群 UUID 和节点健康状态。
- 对同一 `hostname:port` 的并发修改进行进程内串行化。
- 记录不包含密码和完整连接串的结构化应用日志。

当前实现不包括：

- 不读取或修改 Galera 节点的 `my.cnf`、`server.cnf`、容器配置或其他持久化配置。
- 不保证节点重启后保留修改结果。
- 不批量修改多个节点。
- 不执行集群仲裁预演或自动计算推荐权重。
- 不从其他集群节点交叉验证修改结果。
- 不执行集群 Bootstrap、`pc.bootstrap`、节点恢复或服务重启。
- 不新增独立授权 Policy；页面和对话框使用现有 `[Authorize]`。
- 不新增业务审计实体、数据库表、EF Migration 或审计页面。
- 不提供 SSH、Kubernetes、Ansible 等远程配置管理能力。

## 3. 页面行为

### 3.1 Main TAB

Main TAB 继续管理 ProxySQL Main 配置：

- 展示 `mysql_galera_hostgroups` 和 `mysql_servers` 关联结果。
- 允许编辑和删除 Main 成员。
- 不展示 Galera 仲裁权重列。
- 不提供 Galera 仲裁权重修改入口。

### 3.2 Runtime TAB

Runtime TAB 使用以下 ProxySQL 表：

- `runtime_mysql_galera_hostgroups`
- `runtime_mysql_servers`

Runtime TAB 的成员表：

- 展示 ProxySQL 路由权重。
- 额外展示 Galera 仲裁权重。
- 提供“Quorum weight”按钮。
- 保留连接池入口。

页面顶部的“此层只读”表示 ProxySQL Runtime 表本身不能通过该页面编辑。“Quorum weight”操作修改的是目标 Galera 节点进程中的运行值，不是 ProxySQL Runtime 表。

### 3.3 Disk TAB

Disk TAB保持只读：

- 不展示 Galera 仲裁权重列。
- 不提供 Galera 仲裁权重修改入口。

## 4. 节点来源与去重

系统不会从环境变量读取 Galera 节点地址，也没有独立节点发现子系统。

节点端点来自当前 Runtime Galera Hostgroup 概览中的成员：

```text
runtime_mysql_galera_hostgroups
        +
runtime_mysql_servers
        ↓
hostname:port
```

读取权重前，页面按以下键去重：

```text
hostname:port
```

同一物理节点出现在 Writer、Backup Writer、Reader 或 Offline Hostgroup 多行时：

- 一次页面加载只直连读取一次。
- 读取结果存入以 `hostname:port` 为键的页面状态字典。
- 所有引用该端点的行显示同一个运行时权重和状态。

服务端仍要求调用携带的 `hostgroupId + hostname + port` 精确存在于 Runtime Galera 拓扑中。节点在打开页面后被移出 Runtime 拓扑时，后续读取或修改会被拒绝。

## 5. 配置

### 5.1 环境变量

Galera 节点管理凭据使用两个独立环境变量：

```text
PAI_GALERA_USERNAME
PAI_GALERA_PASSWORD
```

应用通过 `.AddEnvironmentVariables("PAI_")` 加载环境变量，因此服务代码读取的配置键是：

```csharp
configuration["GALERA_USERNAME"]
configuration["GALERA_PASSWORD"]
```

两个变量必须同时非空，功能才被视为已配置。未配置时：

- ProxySQL 其他管理功能仍可使用。
- Runtime 表中的“Galera 仲裁权重”列显示“未配置”。
- 权重修改按钮禁用。
- 不尝试连接任何 Galera 节点。

### 5.2 Visual Studio User Secrets

开发环境通过 User Secrets 配置时，键名不带 `PAI_` 前缀：

```json
{
  "GALERA_USERNAME": "galera_admin",
  "GALERA_PASSWORD": "实际密码"
}
```

### 5.3 ProxySQL Admin 连接

ProxySQL Admin 连接与 Galera 节点凭据完全分离：

```text
PAI_PROXYSQL
```

系统不从 ProxySQL `mysql_users.password` 动态提取 Galera 管理凭据，也不将 ProxySQL Admin 连接串用于 Galera 节点直连。

## 6. 节点连接参数

系统使用 `MySqlConnectionStringBuilder` 为每个目标端点组装短生命周期连接：

| 参数 | 当前值 |
| --- | --- |
| `Server` | Runtime 成员的 `hostname` |
| `Port` | Runtime 成员的 `port` |
| `UserID` | `GALERA_USERNAME` |
| `Password` | `GALERA_PASSWORD` |
| `SslMode` | `Preferred` |
| `ConnectionTimeout` | 5 秒 |
| `DefaultCommandTimeout` | 10 秒 |
| `Pooling` | `false` |

`SslMode=Preferred` 表示：

- 节点支持 TLS 时优先使用 TLS。
- 节点不支持 TLS 时允许回退到非 TLS。

因此生产环境仍应在数据库侧正确配置 TLS；当前代码不会强制拒绝非 TLS 连接。

## 7. 运行状态读取

服务直连节点后执行只读查询。

### 7.1 全局变量

```sql
SHOW GLOBAL VARIABLES WHERE Variable_name IN (
    'wsrep_on',
    'wsrep_node_name',
    'wsrep_cluster_name',
    'wsrep_provider_options'
);
```

### 7.2 全局状态

```sql
SHOW GLOBAL STATUS WHERE Variable_name IN (
    'wsrep_cluster_state_uuid',
    'wsrep_cluster_status',
    'wsrep_local_state_comment',
    'wsrep_ready',
    'wsrep_connected'
);
```

### 7.3 服务器身份和授权

```sql
SELECT @@version, @@version_comment;
SHOW GRANTS FOR CURRENT_USER;
```

如果 `SHOW GRANTS FOR CURRENT_USER` 被数据库拒绝，读取操作仍可继续，写权限状态记为未知。实际修改命令仍是最终权限检查。

如果目标节点没有返回 `wsrep_on`，系统认为该端点没有暴露 Galera WSREP 变量并终止操作。

## 8. `pc.weight` 解析

系统从完整的 `wsrep_provider_options` 字符串中解析 `pc.weight`。

解析规则：

- 以分号拆分 Provider Options。
- 键名大小写不敏感。
- 忽略键和值两侧的空格。
- 忽略与 `pc.weight` 无关的选项。
- 权重必须是 `0..255` 的整数。
- 出现多个 `pc.weight` 时视为无效并报错。
- `wsrep_provider_options` 为空或未显式包含 `pc.weight` 时，返回 Galera 默认值 `1`，并标记为“默认值”。

页面显示的是解析后的节点运行值，不使用 ProxySQL 的 `mysql_servers.weight` 代替。

## 9. 页面加载流程

Runtime TAB 首次加载或手动刷新时：

1. 从 ProxySQL 读取 Runtime Galera Hostgroup 定义和 Runtime MySQL Servers。
2. 构建 Hostgroup 成员视图。
3. 按 `hostname:port` 去重可用成员。
4. 将每个端点状态设置为“读取中”。
5. 并行调用 `GaleraNodeWeightService.InspectAsync(...)`。
6. 服务端再次确认端点属于 Runtime Galera 拓扑。
7. 直连节点并读取 WSREP 状态和 `pc.weight`。
8. 将结果写入端点状态字典。
9. 同一端点对应的多行共享显示结果。

单个节点读取失败只影响该端点，其他节点仍继续读取。失败行显示“读取失败”，详细脱敏错误保存在提示文本中。

## 10. 健康状态判断

节点只有同时满足以下条件时才允许提交权重修改：

```text
wsrep_on = ON
wsrep_connected = ON
wsrep_ready = ON
wsrep_cluster_status = Primary
wsrep_local_state_comment = Synced
```

对应代码属性为 `GaleraNodeWeightViewModel.IsHealthyForChange`。

健康状态只判断目标节点当前报告的状态。当前实现不会读取其他节点来确认整个集群是否健康，也不会根据集群规模评估权重修改后的仲裁风险。

## 11. 修改对话框

用户在 Runtime TAB 点击“Quorum weight”后，对话框重新读取节点状态，不直接信任表格缓存。

对话框展示：

- 节点 `hostname:port`
- `wsrep_node_name`
- `wsrep_cluster_name`
- `wsrep_cluster_state_uuid`
- 当前 `pc.weight`
- 当前值是否来自默认值
- `Primary / Synced` 状态
- WSREP、Connected、Ready 状态
- 写权限已识别或将在提交时检查
- 运行时修改和重启后可能丢失的提示

输入约束：

- 新权重范围为 `0..255`。
- 新值必须与当前值不同。
- 节点必须满足健康状态。

对话框使用现有登录用户名称作为应用日志中的操作人字段。

## 12. 修改流程

服务端修改流程如下：

1. 校验新权重属于 `0..255`。
2. 确认 `hostgroupId + hostname + port` 仍存在于 Runtime Galera 拓扑。
3. 按 `hostname:port` 获取进程内 `SemaphoreSlim` 锁。
4. 重新直连目标节点读取当前状态。
5. 比较对话框打开时记录的节点名称、集群名称、集群 UUID 和旧权重。
6. 校验节点仍为 `Primary / Synced / Ready / Connected` 且 WSREP 开启。
7. 如果 `SHOW GRANTS` 明确识别到全局写权限，则显示已识别；无法识别时仍由实际写命令判断。
8. 拒绝与当前值相同的新权重。
9. 参数化执行单项 Provider Option 更新。
10. 重新读取目标节点。
11. 验证读取到的新权重等于请求值。
12. 验证集群 UUID 没有变化。
13. 验证目标节点修改后仍满足健康条件。
14. 记录成功日志并返回修改前后状态。
15. 释放端点锁。

实际写入语句：

```sql
SET GLOBAL wsrep_provider_options = @providerOptions;
```

参数值由服务端生成：

```text
pc.weight=<0..255>
```

系统不会把读取到的完整 `wsrep_provider_options` 原样回写，因此不会主动提交其他 Provider Option。

## 13. 并发控制

服务使用静态字典保存端点锁：

```text
ConcurrentDictionary<string, SemaphoreSlim>
```

锁键为：

```text
hostname:port
```

该锁只能防止同一应用进程内对同一节点并发修改，不能防止：

- 多个应用实例同时修改同一节点。
- 其他管理工具直接修改该节点。
- 数据库管理员在命令行中同时执行修改。

因此服务在获得锁后仍会重新读取并校验节点身份、集群 UUID 和旧权重。

## 14. 权限判断

系统通过 `SHOW GRANTS FOR CURRENT_USER` 对以下授权文本进行启发式识别：

- `ALL PRIVILEGES ON *.*`
- `SUPER`
- `SYSTEM_VARIABLES_ADMIN`

匹配到其中之一时，`HasWritePrivilege=true`。

未匹配或无法执行 `SHOW GRANTS` 时，当前实现返回未知，而不是明确返回无权限。提交时由数据库执行 `SET GLOBAL wsrep_provider_options` 的结果进行最终判断。

## 15. 错误处理

MySQL 驱动异常会记录到服务端应用日志，并向页面返回脱敏错误。

当前明确映射：

| MySQL 错误号 | 页面错误 |
| --- | --- |
| `1045` | 配置账号认证失败 |
| `1044` | 配置账号没有访问权限 |
| `2002`、`2003` | 节点不可达 |
| 其他 | 数据库拒绝操作 |

应用日志包含目标端点和异常堆栈，但结构化业务日志不记录密码、完整连接串或完整 Provider Options。

## 16. 日志

修改请求日志记录：

- Web 登录用户名
- 目标 `hostname:port`
- Galera 节点名称
- 集群 UUID
- 旧权重
- 新权重

修改成功日志记录：

- 目标 `hostname:port`
- 节点名称
- 集群 UUID
- 修改后读取到的权重

修改失败日志记录：

- 目标 `hostname:port`
- Web 登录用户名
- 服务端异常

当前没有独立审计数据库，也没有审计查询页面。

## 17. 运行时语义

本功能只管理节点进程当前生效的 `pc.weight`：

- 不读取节点配置文件中的权重。
- 不判断配置文件是否与运行值一致。
- 不自动持久化修改。
- 不在节点重启后自动恢复本次设置。

因此对话框固定提示：修改仅影响当前运行中的 Galera 进程，节点重启后可能丢失。

## 18. 安全边界

当前实现的安全边界：

- 页面和对话框要求用户已登录。
- 数据库密码只保存在服务器端配置中，不发送到浏览器。
- 目标地址只能来自 ProxySQL Runtime Galera 拓扑。
- 写入值由服务端根据已校验整数生成。
- 写命令使用参数传递 Provider Option 字符串。
- 修改前后均重新读取节点状态。
- 同一端点在单应用进程内串行修改。

当前限制：

- 没有独立的 `GaleraClusterAdmin` 角色或 Policy。
- 没有多实例分布式锁。
- 没有跨节点仲裁安全计算。
- `SslMode=Preferred` 允许非 TLS 回退。
- 没有业务级持久化审计。

## 19. 主要代码位置

| 文件 | 职责 |
| --- | --- |
| `Components/Pages/MySql/GaleraHostgroups/MysqlGaleraHostgroupsPage.razor` | Runtime 权重列、端点去重、并行读取、操作入口和刷新 |
| `Components/Pages/MySql/GaleraHostgroups/EditGaleraNodeWeightDialog.razor` | 高风险操作对话框和提交交互 |
| `Services/GaleraNodeWeightService.cs` | Runtime 成员校验、节点直连、状态读取、修改、并发锁和日志 |
| `Services/GaleraProviderOptionsParser.cs` | 从 Provider Options 解析 `pc.weight` |
| `ViewModel/GaleraNodeWeightViewModel.cs` | 节点状态、修改请求、修改结果和行状态模型 |
| `Repositories/ProxySqlRepository.cs` | 读取 Runtime Galera Hostgroup 和 Runtime MySQL Server 拓扑 |
| `Services/LocalizationService.cs` | 页面及错误消息的中文本地化 |
| `Program.cs` | 配置源和 `GaleraNodeWeightService` 注册 |

## 20. 当前验收口径

当前代码满足以下行为时，功能符合本文档：

- Main 和 Disk TAB 不显示 Galera 仲裁权重。
- Runtime TAB 显示节点当前运行中的 `pc.weight`。
- 相同 `hostname:port` 在一次页面加载中只读取一次。
- 未配置两个 Galera 凭据时不连接节点，功能保持禁用。
- 读取前和修改前都确认目标属于 Runtime Galera 拓扑。
- 缺失显式 `pc.weight` 时显示默认值 `1`。
- 重复、非法或越界的 `pc.weight` 不被当作有效值。
- 非 Primary、非 Synced、未 Ready、未 Connected 或未开启 WSREP 的节点不能修改。
- 新权重只接受 `0..255`，且不能等于当前值。
- 修改命令只提交 `pc.weight=<value>`。
- 修改后必须重新读取并验证新权重、集群 UUID 和健康状态。
- 修改不会触碰 ProxySQL 路由权重或节点持久化配置。
