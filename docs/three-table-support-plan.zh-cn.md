# 三层配置页面支持方案

## 1. 结论

现有页面基本都是“一个页面读取一张表”，首期可以直接改造成 Main、Runtime、Disk 三个页签，不必先实现复杂的三层记录合并和字段级对比。

- Main：可读写，保留现有新增、编辑、删除操作。
- Runtime：只读，用于查看当前实际生效的配置。
- Disk：只读，用于查看重启后会恢复的持久化配置。

适合改造成三页签的页面有四个：后端服务器、用户、查询规则、全局变量。查询摘要来自 Stats 统计库，不存在 Main、Runtime、Disk 三层，因此继续保留单表页面。

| 页面 | Main | Runtime | Disk | 页面处理 |
| --- | --- | --- | --- | --- |
| 后端服务器 | `mysql_servers` | `runtime_mysql_servers` | `disk.mysql_servers` | 三页签，只有 Main 可编辑 |
| 用户 | `mysql_users` | `runtime_mysql_users` | `disk.mysql_users` | 三页签，只有 Main 可编辑 |
| 查询规则 | `mysql_query_rules` | `runtime_mysql_query_rules` | `disk.mysql_query_rules` | 三页签，只有 Main 可编辑 |
| 全局变量 | `global_variables` | `runtime_global_variables` | `disk.global_variables` | 三页签，只有 Main 可编辑 |
| 查询摘要 | - | `stats_mysql_query_digest` | - | 保持现有 Stats 单表页面 |

不同 ProxySQL 版本可能存在表和列结构差异，实施时仍需在目标版本上验证以上表名和字段。

## 2. 页面设计

### 2.1 使用三个页签

每个配置页面使用相同结构：

```text
[ Main（可编辑） ] [ Runtime（只读） ] [ Disk（只读） ]
```

建议使用 RadzenTabs，而不是把三张宽表同时横向摆放：

- 默认打开 Main，保持用户现有操作习惯。
- Main 显示新增、编辑、删除按钮。
- Runtime 和 Disk 不显示任何写操作。
- 每个页签明确显示表名和只读状态，避免用户误认为正在编辑生效配置。
- 页签首次打开时再加载对应数据，切换页签不重复查询；“刷新”只刷新当前页签。
- Main 修改并完成同步后，清除 Runtime、Disk 页签缓存；用户再次打开时读取最新数据。
- 表不存在、权限不足和空数据必须显示为不同状态。

三个页签可以继续复用现有页面的 DataGrid 列定义。首期不需要增加独立的对比服务，也不需要把三层记录合并到一张表中。

### 2.2 各页面改造范围

#### 后端服务器

`MysqlServersPage.razor` 显示全部 `mysql_servers`，作为新增服务器和管理未归属拓扑服务器的统一入口。Replication/Galera 主机组页面只显示各自定义所引用的 Hostgroup 成员，不能用它们替代完整服务器列表。

#### 用户

现有 `MysqlUsersPage.razor` 的列表作为 Main 页签内容，Runtime 和 Disk 使用相同列，但隐藏操作列。只读页签和日志中均不得额外暴露密码；如果现有 DataGrid 不显示密码，继续保持。

#### 查询规则

现有查询规则列表作为 Main 页签内容。Runtime、Disk 首期只读取对应规则表，不复用 Main 查询中与 `stats_mysql_query_rules`、`stats_mysql_query_digest` 的关联统计，避免把运行统计误当作配置层字段。

#### 全局变量

现有全局变量列表作为 Main 页签内容，Runtime、Disk 显示同样的变量名和值，并保持只读。

全局变量与前三类配置不同：一张 `global_variables` 表中包含不同变量类别，同步命令取决于变量名前缀。例如：

```sql
LOAD ADMIN VARIABLES TO RUNTIME;
SAVE ADMIN VARIABLES TO DISK;

LOAD MYSQL VARIABLES TO RUNTIME;
SAVE MYSQL VARIABLES TO DISK;
```

因此不能在修改任意全局变量后固定执行 `LOAD ADMIN VARIABLES TO RUNTIME`。保存时必须根据受影响变量的类别选择白名单命令；不支持的前缀应拒绝自动同步并给出明确提示。具体支持哪些变量类别，应以目标 ProxySQL 版本为准。

当前 `UpdateGlobalVariable` 只固定执行 `LOAD ADMIN VARIABLES TO RUNTIME`，且没有执行对应的 `SAVE ... TO DISK`，实施本方案时需要一并修正。

#### 查询摘要

`stats_mysql_query_digest` 是运行统计，不是配置编辑区。该页面继续使用现有单表 DataGrid、筛选、刷新、重置统计和创建规则等操作，不增加 Main、Runtime、Disk 页签。

## 3. 最小后端改造

### 3.1 层级枚举和固定映射

新增层级枚举：

```csharp
public enum ProxySqlConfigLayer
{
    Main,
    Runtime,
    Disk
}

public enum ProxySqlConfigTable
{
    MysqlServers,
    MysqlUsers,
    MysqlQueryRules,
    GlobalVariables
}
```

表名不能使用普通 SQL 参数，因此必须通过代码中的固定白名单取得，禁止把 URL、查询参数或用户输入直接拼入 SQL：

```csharp
private static readonly IReadOnlyDictionary<(ProxySqlConfigLayer Layer, ProxySqlConfigTable Table), string> TableMap =
    new Dictionary<(ProxySqlConfigLayer, ProxySqlConfigTable), string>
    {
        [(ProxySqlConfigLayer.Main, ProxySqlConfigTable.MysqlServers)] = "mysql_servers",
        [(ProxySqlConfigLayer.Runtime, ProxySqlConfigTable.MysqlServers)] = "runtime_mysql_servers",
        [(ProxySqlConfigLayer.Disk, ProxySqlConfigTable.MysqlServers)] = "disk.mysql_servers",

        [(ProxySqlConfigLayer.Main, ProxySqlConfigTable.MysqlUsers)] = "mysql_users",
        [(ProxySqlConfigLayer.Runtime, ProxySqlConfigTable.MysqlUsers)] = "runtime_mysql_users",
        [(ProxySqlConfigLayer.Disk, ProxySqlConfigTable.MysqlUsers)] = "disk.mysql_users",

        [(ProxySqlConfigLayer.Main, ProxySqlConfigTable.MysqlQueryRules)] = "mysql_query_rules",
        [(ProxySqlConfigLayer.Runtime, ProxySqlConfigTable.MysqlQueryRules)] = "runtime_mysql_query_rules",
        [(ProxySqlConfigLayer.Disk, ProxySqlConfigTable.MysqlQueryRules)] = "disk.mysql_query_rules",

        [(ProxySqlConfigLayer.Main, ProxySqlConfigTable.GlobalVariables)] = "global_variables",
        [(ProxySqlConfigLayer.Runtime, ProxySqlConfigTable.GlobalVariables)] = "runtime_global_variables",
        [(ProxySqlConfigLayer.Disk, ProxySqlConfigTable.GlobalVariables)] = "disk.global_variables"
    };
```

### 3.2 Repository 方法

在现有读取方法上增加可选层级参数，默认值为 Main，减少对现有调用方的影响：

```csharp
Task<IReadOnlyList<MysqlServerModel>> GetMySqlServers(
    ProxySqlConfigLayer layer = ProxySqlConfigLayer.Main);

Task<IReadOnlyList<MysqlUserModel>> GetMySqlUsers(
    ProxySqlConfigLayer layer = ProxySqlConfigLayer.Main);

Task<IReadOnlyList<MysqlQueryRuleModel>> GetMySqlQueryRules(
    ProxySqlConfigLayer layer = ProxySqlConfigLayer.Main);

Task<IReadOnlyList<GlobalVariableModel>> GetGlobalVariables(
    ProxySqlConfigLayer layer = ProxySqlConfigLayer.Main);
```

所有 Runtime 和 Disk 查询使用 `AsNoTracking()`。所有新增、更新、删除方法继续只操作 Main，不接收层级参数，避免误写只读层。

## 4. 保存和同步

服务器、用户和查询规则继续使用标准流程：

```text
写入 Main
  -> LOAD ... TO RUNTIME
  -> SAVE ... TO DISK
  -> 标记 Runtime、Disk 页签需要重新加载
```

对应命令为：

```sql
LOAD MYSQL SERVERS TO RUNTIME;
SAVE MYSQL SERVERS TO DISK;

LOAD MYSQL USERS TO RUNTIME;
SAVE MYSQL USERS TO DISK;

LOAD MYSQL QUERY RULES TO RUNTIME;
SAVE MYSQL QUERY RULES TO DISK;
```

全局变量按变量类别执行对应的 `LOAD` 和 `SAVE` 命令。任何步骤失败都要显示真实状态：

- Main 写入失败：配置未修改。
- LOAD 失败：Main 已修改，但 Runtime 尚未生效。
- SAVE 失败：Runtime 可能已经生效，但 Disk 尚未持久化。

首期可以沿用现有“保存即 LOAD 并 SAVE”的行为，不必额外增加复杂状态机；但错误提示必须区分上述阶段。

## 5. 实施顺序

### 阶段一：Repository 支持三层读取

- 增加层级枚举、配置表枚举和固定映射。
- 为四类配置增加带层级参数的读取方法。
- 保留 Main 默认值，确保现有页面在改造期间仍可工作。
- 在目标 ProxySQL 版本验证十二个表引用及列结构。

### 阶段二：四个页面增加三页签

- 先改造 Servers 页面并提取可复用的页签加载模式。
- 按相同方式改造 Users、Query Rules、Global Variables。
- Main 保留原有写操作，Runtime、Disk 只读。
- 查询摘要页面不做三层改造。

### 阶段三：修正同步和错误反馈

- 保存后使 Runtime、Disk 的已加载数据失效。
- 为 LOAD 失败和 SAVE 失败提供不同提示。
- 修正全局变量同步命令，按变量类别执行 LOAD 和 SAVE。
- 对不支持的表或变量类别显示具体原因。

### 后续可选：三层差异对比

只有在三页签实际使用后仍有强需求，再增加：

- 差异记录计数。
- 只显示有差异的数据。
- 字段级差异详情。

这些功能不是首期三层展示的前置条件。

## 6. 测试方案

- 映射测试：每个层级和业务表映射到正确且固定的 SQL 表名。
- Repository 测试：四类配置均能读取 Main、Runtime、Disk。
- 页面测试：默认打开 Main；Runtime、Disk 不显示新增、编辑、删除操作。
- 懒加载测试：未打开的页签不查询，刷新只更新当前页签。
- 同步测试：Main 修改后执行正确的 LOAD 和 SAVE，并重新读取只读页签。
- 全局变量测试：`admin-`、`mysql-` 和不支持前缀分别走正确分支。
- 错误测试：区分空数据、表不存在、权限不足、LOAD 失败和 SAVE 失败。
- 安全测试：表名只来自白名单，用户密码不出现在日志和只读视图中。

## 7. 推荐落地决策

首期直接把四个配置页面改成 Main、Runtime、Disk 三页签即可，不实现三层合并对比。查询摘要维持 Stats 单表页面。这样改动与当前页面结构最接近，也能立即回答“编辑的配置是什么、当前生效的是什么、磁盘保存的是什么”三个核心问题。
