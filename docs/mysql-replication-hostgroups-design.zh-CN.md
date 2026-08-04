# ProxySQL Replication 主机组定义显示设计

本文只描述传统 `mysql_replication_hostgroups`。Galera 使用独立的
`mysql_galera_hostgroups` 页面和模型，详见
[Galera 主机组显示设计](mysql-galera-hostgroups-design.zh-CN.md)。两种拓扑不能混用。

## 1. 当前背景

`mysql_servers.hostgroup_id` 只是后端所属的主机组编号。编号本身没有“主写”“备写”或“读”的固定含义，角色来自 ProxySQL 的拓扑定义表。

独立 MySQL Servers 页面负责显示和管理全部 `mysql_servers`；Replication 页面只显示定义引用的
Writer/Reader Hostgroup 成员，并支持 Main、Runtime、Disk 三个页签：

| 层级 | 成员表 | 含义 |
| --- | --- | --- |
| Main | `mysql_servers` | 配置编辑区 |
| Runtime | `runtime_mysql_servers` | 当前已加载并正在使用的成员 |
| Disk | `disk.mysql_servers` | 持久化配置 |

因此，主机组页面不再设计成独立的“配置视图 / 生效视图”切换，而是复用相同的三层页签，在同一层级同时读取角色定义和成员。

页面路由为 `/mysql/replication-hostgroups`（兼容 `/mysql/hostgroups`），用于回答：

- 某个 hostgroup ID 被定义成 Writer 还是 Reader？
- 这个定义来自哪一行配置？
- 当前层级有哪些成员属于该组？
- Writer/Reader 定义引用的 Hostgroup 是否有成员？
- Main、Runtime、Disk 中的定义和成员是否分别存在？

## 2. ProxySQL 角色定义

### 2.1 `mysql_replication_hostgroups` 的真实 schema

在当前支持的 ProxySQL 版本中，`mysql_replication_hostgroups` 及其 Runtime、Disk 表包含以下字段：

| 字段 | 含义 |
| --- | --- |
| `writer_hostgroup` | Writer 主机组 ID |
| `reader_hostgroup` | Reader 主机组 ID |
| `check_type` | Monitor 检查类型 |
| `comment` | 管理员备注 |

当前版本不应从这张表读取以下字段：

- `backup_writer_hostgroup`
- `offline_hostgroup`
- `max_writers`
- `max_transactions_behind`

这些字段不能假设存在。不同拓扑表（例如 Group Replication 或 Galera）可能有不同的列结构，后续支持时应使用独立模型和独立查询。

角色定义示例：

```sql
INSERT INTO mysql_replication_hostgroups
    (writer_hostgroup, reader_hostgroup, check_type, comment)
VALUES
    (10, 30, 'read_only', '业务 MySQL 复制集群');
```

这表示 `10` 是 Writer 主机组，`30` 是 Reader 主机组。不是因为数字 `10` 或 `30` 有特殊语义。

### 2.2 Monitor 与 Writer/Reader 重叠

Monitor 会根据角色定义和后端状态调整 Runtime 中的成员归属。`mysql-monitor_writer_is_also_reader` 为真时，同一个物理后端可能同时出现在 Writer 和 Reader 组中，这是合法的成员关系，不应直接标记为角色冲突。

需要区分两个概念：

- 同一物理后端出现在两个不同主机组：可以是合法的 Writer/Reader 重叠。
- 同一个 hostgroup ID 同时出现在 `writer_hostgroup` 和 `reader_hostgroup`：该 ID 的角色无法唯一解释，显示为角色冲突。

页面展示的“角色冲突”只针对第二种情况。

## 3. 页面设计

### 3.1 三个页签

Hostgroups 页面使用与现有配置页面一致的页签：

```text
[ Main（只读） ] [ Runtime（只读） ] [ Disk（只读） ]
```

页面不允许直接编辑 Hostgroup 定义，避免误触发读写拓扑切换；Main 层允许编辑和删除已关联的
`mysql_servers` 成员，新增服务器统一从 MySQL Servers 页面完成。保存后加载到 Runtime 并持久化
到 Disk。每个页签同时展示：

1. 当前层级的 `mysql_replication_hostgroups` 定义。
2. 同一层级的 `mysql_servers` 成员。
3. 当前层级的 `mysql-monitor_writer_is_also_reader` 值（如可读取）。

页签行为：

- 默认打开 Main。
- 页签首次打开时按需读取，之后复用已加载结果。
- 刷新只刷新当前页签。
- 表不存在、权限不足、查询失败和空数据分别展示。
- 页面显示固定的数据源表名，避免把 Main、Runtime、Disk 混淆。

### 3.2 角色定义区域

显示 `mysql_replication_hostgroups` 的所有行：

| 页面字段 | 数据字段 |
| --- | --- |
| Writer 主机组 | `writer_hostgroup` |
| Reader 主机组 | `reader_hostgroup` |
| 检查类型 | `check_type` |
| 备注 | `comment` |

没有定义行时显示“当前层级未配置复制主机组定义”，不能把空数据解释成默认的 10/20/30/40 角色。

### 3.3 成员区域

按 `hostgroup_id` 展示同层级的 `mysql_servers`：

| 页面字段 | 来源 |
| --- | --- |
| 主机组 ID | `hostgroup_id` |
| 角色 | 由定义表推断 |
| 角色来源 | `writer_hostgroup` / `reader_hostgroup` |
| 主机名 | `hostname` |
| 端口 | `port` |
| 状态 | `status` |
| 权重 | `weight` |
| 最大连接数 | `max_connections` |
| 最大复制延迟 | `max_replication_lag` |

角色状态：

- Writer：只命中 `writer_hostgroup`。
- Reader：只命中 `reader_hostgroup`。
- 角色冲突：同一 ID 同时命中 Writer 和 Reader。
- 未被当前定义引用的 Hostgroup 不在本页面显示，应在 MySQL Servers 页面管理。

定义中有 ID 但成员为空时，仍显示一行“未配置成员”，帮助用户发现配置和成员之间的缺口。

## 4. 数据查询

### 4.1 定义表

层级到表名必须来自代码白名单：

| 层级 | 定义表 |
| --- | --- |
| Main | `mysql_replication_hostgroups` |
| Runtime | `runtime_mysql_replication_hostgroups` |
| Disk | `disk.mysql_replication_hostgroups` |

查询使用固定列，不使用 `SELECT *`，避免版本增加或减少列时破坏模型映射：

```sql
SELECT writer_hostgroup,
       reader_hostgroup,
       check_type,
       comment
FROM mysql_replication_hostgroups
ORDER BY writer_hostgroup, reader_hostgroup;
```

Runtime 和 Disk 只替换为白名单中的对应表引用。

### 4.2 成员表

成员读取复用现有三层映射：

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

成员和定义必须使用相同层级，不能用 Main 定义关联 Runtime 成员后再把结果称为单层配置。

### 4.3 Monitor 变量

在同一层级读取 `mysql-monitor_writer_is_also_reader`：

- Main：`global_variables`
- Runtime：`runtime_global_variables`
- Disk：`disk.global_variables`

变量不存在或没有权限时显示“设置不可用”。不把变量读取失败当作 `false`。

## 5. 数据模型与角色推断

### 5.1 定义模型

```csharp
[Table("mysql_replication_hostgroups")]
public class MysqlReplicationHostgroupModel
{
    [Column("writer_hostgroup")]
    public int WriterHostgroup { get; set; }

    [Column("reader_hostgroup")]
    public int ReaderHostgroup { get; set; }

    [Column("check_type")]
    public string? CheckType { get; set; }

    [Column("comment")]
    public string? Comment { get; set; }
}
```

### 5.2 角色推断规则

对每个 hostgroup ID 收集所有定义行的引用：

1. 命中 `writer_hostgroup`，加入 Writer 角色。
2. 命中 `reader_hostgroup`，加入 Reader 角色。
3. 只命中 Writer，显示 Writer。
4. 只命中 Reader，显示 Reader。
5. 同时命中 Writer 和 Reader，显示角色冲突。
6. 没有任何引用的服务器不属于该 Replication 定义，不加入成员结果。

角色推断只针对主机组 ID。物理后端同时出现在 Writer 和 Reader 两个不同 ID 中不属于该规则的冲突。

## 6. Repository 与页面实现

### 6.1 固定映射

在现有 `ProxySqlConfigTable` 中加入 `MysqlReplicationHostgroups`，并加入 Main、Runtime、Disk 三个固定表引用。页面只能通过 Repository 取得数据源表名，不能拼接用户输入。

### 6.2 Repository 方法

增加：

```csharp
Task<IReadOnlyList<MysqlReplicationHostgroupModel>> GetMySqlReplicationHostgroups(
    ProxySqlConfigLayer layer = ProxySqlConfigLayer.Main);

Task<HostgroupOverviewViewModel> GetHostgroupOverview(
    ProxySqlConfigLayer layer = ProxySqlConfigLayer.Main);
```

`GetHostgroupOverview` 的流程：

1. 读取同层级的角色定义。
2. 读取同层级的 `mysql_servers`。
3. 读取同层级的 Monitor 变量。
4. 建立 ID 到角色集合的映射。
5. 仅合并定义引用的 Hostgroup；定义中的空组仍生成占位行。
6. 生成成员展示行。

### 6.3 页面入口

在 MySQL 菜单增加：

```text
MySQL Hostgroups
路由：/mysql/hostgroups
```

页面组件：

```text
ProxysqlAdminUi.Web/Components/Pages/MySql/Hostgroups/MySqlHostgroupsPage.razor
```

## 7. 后续阶段

以下内容不作为本次第一期的前置条件：

### 阶段二：现有页面显示角色名称

- Servers 的 Hostgroup 列增加 Writer/Reader 标签。
- Users 的 Default HG 增加角色名称。
- Query Rules 的 Destination、Mirror、GTID Hostgroup 增加角色名称。
- Query Digest 的 Hostgroup 增加角色名称。

这些页面需要决定使用当前页签的定义层级，不能把 Main 角色名称套到 Runtime 或 Disk 数据上。

### 阶段三：其他拓扑

Galera 已作为独立页面实现：

- `mysql_galera_hostgroups`
- 页面路由：`/mysql/galera-hostgroups`
- 页面菜单名：Replication Hostgroups 与 Galera Hostgroups 分开显示

后续仍可独立支持：

- `mysql_group_replication_hostgroups`

每种拓扑必须先探测表和列结构，再使用独立模型；不能把它们强行映射成 `mysql_replication_hostgroups` 的四列模型。

### 阶段四：跨层差异

如果确有需要，再增加 Main / Runtime / Disk 的差异计数和字段级详情。本阶段的三页签只负责可靠读取和展示，不要求跨层合并。

## 8. 验收标准

- 新增 `/mysql/hostgroups` 页面并出现在 MySQL 菜单。
- 页面有 Main、Runtime、Disk 三个只读页签。
- 每个页签读取同层级的定义表、成员表和 Monitor 变量。
- 页面使用当前 ProxySQL 版本实际存在的四列定义 schema。
- 能显示 Writer、Reader 和角色冲突，并排除未被定义引用的 Hostgroup。
- 定义中有组但没有成员时显示“未配置成员”。
- 没有定义行时显示明确的空配置状态。
- 同一物理后端出现在不同 Writer/Reader 主机组时不误报为主机组 ID 冲突。
- 表不存在、权限不足和查询失败显示错误，不伪造默认角色。
- Hostgroup 查询集中在 Repository，页面不直接执行 SQL。
- 不允许从 Hostgroups 页面修改拓扑定义，但允许在 Main 层编辑和删除已关联成员。

## 9. 关系示例

```text
mysql_replication_hostgroups
    -> writer_hostgroup = 10
    -> reader_hostgroup = 30

mysql_servers
    -> hostgroup_id = 10 的后端显示 Writer
    -> hostgroup_id = 30 的后端显示 Reader
    -> 未命中 10 或 30 的后端不属于该定义，不在本页面显示

mysql-monitor_writer_is_also_reader = true
    -> 同一个物理后端可以同时出现在 Writer 和 Reader 成员中
    -> 不改变 10 和 30 各自的角色定义
```
