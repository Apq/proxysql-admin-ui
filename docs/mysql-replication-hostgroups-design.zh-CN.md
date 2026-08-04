# ProxySQL 主机组定义显示设计

## 1. 需求说明

当前项目的 MySQL Servers 页面只显示 `mysql_servers` 中的后端节点。页面可以看到 `hostgroup_id`，但看不出这个数字代表什么业务角色，例如：

- 为什么 `10` 是主写组？
- 为什么 `20` 是备写组？
- 哪个分组是读组？
- 哪些后端属于同一个分组？
- 这个分组的读写切换、复制延迟和最大写节点数限制是什么？

需要在本项目中增加一个“主机组定义”页面，读取 ProxySQL 中主机组角色定义表，并把主机组定义与 `mysql_servers` 中的实际后端成员关联显示。

这里的“分组”指 ProxySQL 的 **Hostgroup（主机组）**，不是 `main`、`runtime`、`disk` 配置层级。

## 2. 核心概念

### 2.1 `hostgroup_id` 没有固定业务含义

`mysql_servers.hostgroup_id` 只是后端服务器所属的主机组编号。`10`、`20`、`30` 等数字是部署者自定义的 ID，ProxySQL 不会因为数字大小自动判断“主写”“备写”或“读”。

例如，下面这条定义才真正赋予了角色含义：

```sql
INSERT INTO mysql_replication_hostgroups
    (writer_hostgroup, reader_hostgroup, backup_writer_hostgroup,
     offline_hostgroup, max_writers, max_transactions_behind, comment)
VALUES
    (10, 30, 20, 40, 1, 100, '业务 MySQL 主从集群');
```

这表示：

| 字段 | 值 | 含义 |
| --- | ---: | --- |
| `writer_hostgroup` | `10` | 写节点主机组，通常是当前主写组 |
| `reader_hostgroup` | `30` | 读节点主机组 |
| `backup_writer_hostgroup` | `20` | 备用写节点主机组，没有被选为当前写节点的候选节点 |
| `offline_hostgroup` | `40` | 不满足拓扑或健康条件时使用的离线主机组 |
| `max_writers` | `1` | 写主机组最多允许的 writer 数量 |
| `max_transactions_behind` | `100` | 复制延迟超过该值时的处理阈值 |

因此，“为什么 10 是主写、20 是备写”的答案不是因为 `10` 或 `20` 有特殊规则，而是因为 `mysql_replication_hostgroups` 的这一行配置了：

```text
writer_hostgroup = 10
backup_writer_hostgroup = 20
```

### 2.2 `mysql_servers` 负责成员，角色定义表负责语义

两张表的职责不同：

```text
mysql_replication_hostgroups
    -> 定义 10、20、30、40 分别是什么角色

mysql_servers
    -> 定义 hostname:port 当前属于哪个 hostgroup_id
```

例如：

```sql
SELECT * FROM mysql_replication_hostgroups;

SELECT hostgroup_id, hostname, port, status, weight, comment
FROM mysql_servers
ORDER BY hostgroup_id, hostname, port;
```

如果 `mysql_servers` 中某台服务器的 `hostgroup_id = 10`，并且角色定义表的 `writer_hostgroup = 10`，那么这台服务器属于写主机组。仅凭 `hostgroup_id = 10` 本身，不能得出它是主写节点。

### 2.3 Monitor 负责根据后端状态调整成员

对于复制集群，ProxySQL Monitor 会检查后端的 `read_only`、复制延迟、连接和 Ping 状态，并由 Hostgroups Manager 根据 `mysql_replication_hostgroups` 的定义调整后端所在的运行时主机组。

典型流程为：

```text
读取 mysql_replication_hostgroups
    -> Monitor 检查后端 read_only 和复制状态
    -> 根据规则调整 runtime_mysql_servers 的 hostgroup_id
    -> 查询路由根据 runtime 主机组转发读写请求
```

所以页面需要同时展示：

- 配置中定义的角色映射。
- 当前 `mysql_servers` 中的成员归属。
- 当前 `runtime_mysql_servers` 中实际生效的成员归属。
- Monitor 相关的健康状态和最近检查结果（如可用）。

## 3. 页面设计

### 3.1 页面入口

在 MySQL 菜单下增加：

```text
MySQL Hostgroups
```

建议路由：

```text
/mysql/hostgroups
```

页面只读展示为第一期目标。角色定义会影响读写路由，暂不在页面上直接编辑，避免误改导致流量切换。

### 3.2 页面上半部分：主机组角色定义

主表显示 `mysql_replication_hostgroups` 的每一行：

| 页面字段 | 数据字段 | 说明 |
| --- | --- | --- |
| 写主机组 | `writer_hostgroup` | 当前写节点所在主机组 ID |
| 读主机组 | `reader_hostgroup` | 读节点所在主机组 ID |
| 备用写主机组 | `backup_writer_hostgroup` | 备用写节点所在主机组 ID |
| 离线主机组 | `offline_hostgroup` | 不可用或不符合拓扑的节点所在主机组 ID |
| 最大写节点数 | `max_writers` | 写节点数量限制 |
| 最大事务延迟 | `max_transactions_behind` | 复制延迟阈值 |
| 是否允许 writer 兼 reader | 全局变量 | `mysql-monitor_writer_is_also_reader` 的当前值 |
| 说明 | `comment` | 管理员对该集群的描述 |

每个 ID 同时显示语义标签，例如：

```text
10  写主机组（Writer）
20  备用写主机组（Backup Writer）
30  读主机组（Reader）
40  离线主机组（Offline）
```

ID 不存在时显示“未配置成员”，而不是把它当作错误。多个复制集群配置行可能使用不同的主机组 ID，页面不能假设全局只有一组 `10/20/30/40`。

### 3.3 页面下半部分：主机组成员

按主机组聚合 `mysql_servers`：

| 主机组 ID | 角色 | 主机名 | 端口 | 状态 | 权重 | 最大连接数 | 复制延迟 | 来源 |
| ---: | --- | --- | ---: | --- | ---: | ---: | ---: | --- |
| 10 | 写主机组 | `db-primary` | 3306 | ONLINE | 100 | 500 | 0 | main/runtime |
| 20 | 备用写主机组 | `db-replica-1` | 3306 | ONLINE | 100 | 500 | 3 | main/runtime |
| 30 | 读主机组 | `db-replica-2` | 3306 | ONLINE | 100 | 500 | 2 | main/runtime |

每一行都应显示“角色来源”：

- `mysql_replication_hostgroups` 映射得到的角色。
- 未被任何角色定义引用时显示“未定义主机组”。
- 同一个 ID 被多个角色字段引用时显示“角色冲突”，并在详情中列出所有引用位置。

### 3.4 运行时与配置的区别

页面提供两个视图：

- 配置视图：读取 `main.mysql_replication_hostgroups` 和 `main.mysql_servers`，表示配置编辑区。
- 生效视图：读取对应的 runtime 表，表示 ProxySQL 当前实际使用的主机组和成员。

页面顶部显示当前视图：

```text
配置视图：main
当前生效视图：runtime
```

如果同一服务器在 `main` 和 `runtime` 中的 `hostgroup_id` 不同，显示警告：

```text
该后端的配置分组与当前生效分组不一致。
```

## 4. 数据查询设计

### 4.1 角色定义查询

主查询：

```sql
SELECT writer_hostgroup,
       reader_hostgroup,
       backup_writer_hostgroup,
       offline_hostgroup,
       max_writers,
       max_transactions_behind,
       comment
FROM mysql_replication_hostgroups
ORDER BY writer_hostgroup, reader_hostgroup;
```

运行时查看时查询：

```sql
SELECT writer_hostgroup,
       reader_hostgroup,
       backup_writer_hostgroup,
       offline_hostgroup,
       max_writers,
       max_transactions_behind,
       comment
FROM runtime_mysql_replication_hostgroups
ORDER BY writer_hostgroup, reader_hostgroup;
```

持久化配置查看时查询：

```sql
SELECT writer_hostgroup,
       reader_hostgroup,
       backup_writer_hostgroup,
       offline_hostgroup,
       max_writers,
       max_transactions_behind,
       comment
FROM disk.mysql_replication_hostgroups
ORDER BY writer_hostgroup, reader_hostgroup;
```

实际支持的 runtime/disk 表名和列结构必须在项目支持的 ProxySQL 版本上验证。文档中的 SQL 是设计目标，不能替代目标实例上的表结构探测。

### 4.2 成员查询

主配置和运行时成员分别读取：

```sql
SELECT hostgroup_id,
       hostname,
       port,
       status,
       weight,
       max_connections,
       max_replication_lag,
       comment
FROM mysql_servers
ORDER BY hostgroup_id, hostname, port;
```

```sql
SELECT hostgroup_id,
       hostname,
       port,
       status,
       weight,
       max_connections,
       max_replication_lag,
       comment
FROM runtime_mysql_servers
ORDER BY hostgroup_id, hostname, port;
```

### 4.3 与查询规则关联

查询规则页面已经显示 `destination_hostgroup`。增加主机组定义页面后，建议把数字 ID 渲染为：

```text
目标主机组：10（写主机组）
```

如果 ID 未在角色定义中出现，仍显示数字并附带“未定义主机组”。这样可以帮助用户理解查询规则为什么会被路由到某个组。

## 5. 数据模型设计

新增模型：

```csharp
[Table("mysql_replication_hostgroups")]
public class MysqlReplicationHostgroupModel
{
    [Column("writer_hostgroup")]
    public int WriterHostgroup { get; set; }

    [Column("reader_hostgroup")]
    public int ReaderHostgroup { get; set; }

    [Column("backup_writer_hostgroup")]
    public int? BackupWriterHostgroup { get; set; }

    [Column("offline_hostgroup")]
    public int? OfflineHostgroup { get; set; }

    [Column("max_writers")]
    public int? MaxWriters { get; set; }

    [Column("max_transactions_behind")]
    public int? MaxTransactionsBehind { get; set; }

    [Column("comment")]
    public string? Comment { get; set; }
}
```

建议增加展示模型，不把角色推断逻辑放入 `MysqlServerModel`：

```csharp
public enum HostgroupRole
{
    Undefined,
    Writer,
    Reader,
    BackupWriter,
    Offline,
    Conflict
}

public sealed class HostgroupMemberViewModel
{
    public int HostgroupId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public HostgroupRole Role { get; set; }
    public List<MysqlServerModel> Servers { get; set; } = [];
}
```

角色推断规则：

1. `hostgroup_id == writer_hostgroup`，角色为 Writer。
2. `hostgroup_id == reader_hostgroup`，角色为 Reader。
3. `hostgroup_id == backup_writer_hostgroup`，角色为 BackupWriter。
4. `hostgroup_id == offline_hostgroup`，角色为 Offline。
5. 同一 ID 命中多个不同角色时，角色为 Conflict。
6. 没有命中任何定义时，角色为 Undefined。

## 6. Repository 和页面改造

### 6.1 `ProxySqlContext`

增加：

```csharp
public DbSet<MysqlReplicationHostgroupModel> MySqlReplicationHostgroups { get; set; }
```

如果 runtime 和 disk 表通过同一个模型读取，建议使用 `Database.SqlQueryRaw<T>`，不要让 EF 模型只绑定到 `main` 表后再隐式切换。

### 6.2 `ProxySqlRepository`

增加以下读取方法：

```csharp
Task<IEnumerable<MysqlReplicationHostgroupModel>> GetMySqlReplicationHostgroups(
    ProxySqlConfigLayer layer = ProxySqlConfigLayer.Main);

Task<IEnumerable<HostgroupMemberViewModel>> GetHostgroupMembers(
    ProxySqlConfigLayer layer = ProxySqlConfigLayer.Main);
```

`GetHostgroupMembers` 的实现步骤：

1. 读取指定层级的 `mysql_replication_hostgroups`。
2. 读取同一层级的 `mysql_servers`。
3. 建立角色 ID 到角色类型的映射。
4. 按 `hostgroup_id` 聚合成员。
5. 生成未定义和角色冲突状态。

### 6.3 页面入口

在 [MainLayout.razor](../ProxysqlAdminUi.Web/Components/Layout/MainLayout.razor) 的 MySQL 菜单下增加：

```razor
<RadzenMenuItem Text="@HostgroupsText" Icon="account_tree" Path="/mysql/hostgroups" />
```

页面组件建议为：

```text
ProxysqlAdminUi.Web/Components/Pages/MySql/Hostgroups/MySqlHostgroupsPage.razor
```

### 6.4 现有页面增强

- Servers 页面：`Hostgroup` 列增加角色名称和定义来源。
- Users 页面：`Default HG` 列增加主机组角色名称。
- Query Rules 页面：`Destination host group`、`Mirror hostgroup` 和 `GTID From Hostgroup` 增加角色名称。
- Query Digest 页面：`Hostgroup` 列增加角色名称。
- 主机组页面：提供从主机组跳转到对应服务器筛选结果的入口。

## 7. 版本和表结构兼容

ProxySQL 不同版本可能支持不同的拓扑表：

- `mysql_replication_hostgroups`：传统 MySQL 复制主从拓扑。
- `mysql_group_replication_hostgroups`：MySQL Group Replication 拓扑。
- `mysql_galera_hostgroups`：Galera 集群拓扑。

第一期建议只实现 `mysql_replication_hostgroups`，但页面模型和 Repository 接口应预留拓扑类型：

```csharp
public enum HostgroupTopologyType
{
    Replication,
    GroupReplication,
    Galera
}
```

进入页面时进行表存在性和列结构探测：

- 表不存在：隐藏该拓扑类型，并显示“当前 ProxySQL 未提供此定义表”。
- 表存在但字段不完整：显示兼容性错误，不使用猜测逻辑。
- 表存在且可读取：加载定义和成员。

## 8. 验收标准

- 新增 `/mysql/hostgroups` 页面。
- 页面能够显示 `mysql_replication_hostgroups` 中的 writer、reader、backup writer 和 offline 映射。
- 页面能够显示每个主机组的成员服务器。
- 页面能明确说明 `10`、`20` 的角色来自哪一行配置，而不是把数字当作固定含义。
- 未定义主机组和角色冲突能够被识别。
- 可切换配置视图和 runtime 生效视图，并显示二者差异。
- 现有服务器、用户、查询规则和查询摘要页面中的 Hostgroup 数字可以显示角色名称。
- 所有 Hostgroup 查询集中在 Repository，页面不直接执行 SQL。
- 在没有 `mysql_replication_hostgroups` 表或权限不足时，页面给出明确错误，不显示误导性的默认角色。
- 第一阶段不允许从该页面直接修改 Hostgroup 定义，避免误触发读写拓扑切换。

## 9. 结论

这个页面的重点不是显示一组数字，而是建立下面这条可解释关系：

```text
mysql_replication_hostgroups
    -> 10 = writer_hostgroup
    -> 20 = backup_writer_hostgroup
    -> 30 = reader_hostgroup

mysql_servers
    -> hostgroup_id = 10 的后端是写组成员
    -> hostgroup_id = 20 的后端是备用写组成员
    -> hostgroup_id = 30 的后端是读组成员
```

只有把“角色定义”和“实际成员”同时展示，用户才能理解 ProxySQL 的分组配置以及 `10`、`20` 为什么具有主写、备写语义。
