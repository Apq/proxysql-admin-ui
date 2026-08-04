# ProxySQL Galera 主机组显示设计

## 1. 适用范围

`mysql_galera_hostgroups` 是 Galera Monitor 使用的拓扑定义表，不能按传统
`mysql_replication_hostgroups` 的四列模型读取。页面只读展示，不在页面内修改拓扑或执行
`LOAD`/`SAVE`。

页面路由为 `/mysql/galera-hostgroups`，菜单分别显示为：

- English: `Galera Hostgroups`
- 中文：`Galera 主机组`

传统复制拓扑位于独立的 `Replication Hostgroups` 页面。

## 2. 三层页签

页面提供三个只读页签：`Main`、`Runtime`、`Disk`。

| 层级 | 数据表 |
| --- | --- |
| Main | `mysql_galera_hostgroups` |
| Runtime | `runtime_mysql_galera_hostgroups` |
| Disk | `disk.mysql_galera_hostgroups` |

每个页签同时读取同层级的 `mysql_servers`，但只保留 `hostgroup_id` 命中当前定义的 Writer、
Backup Writer、Reader、Offline 四个 Hostgroup 的行。其他 Hostgroup 不属于该 Galera 定义，
只能在独立 MySQL Servers 页面查看和管理。

Main 层提供关联成员的编辑、删除和备注维护；新增服务器统一从 MySQL Servers 页面完成。保存后
执行 MySQL Servers 的 LOAD/SAVE。Runtime 与 Disk 仅用于查看，不能直接编辑。
页面固定显示数据源表名，避免误把 Runtime 或 Disk 数据当成 Main 配置。

## 3. 定义字段

页面使用 ProxySQL Galera 表的固定列：

| 字段 | 含义 |
| --- | --- |
| `writer_hostgroup` | 当前 Writer 主机组 |
| `backup_writer_hostgroup` | 备用 Writer 主机组 |
| `reader_hostgroup` | Reader 主机组 |
| `offline_hostgroup` | Offline 主机组 |
| `active` | 是否启用该定义 |
| `max_writers` | 允许的最大 Writer 数 |
| `writer_is_also_reader` | Writer/Reader 重叠策略，保留 ProxySQL 的 0/1/2 值 |
| `max_transactions_behind` | Galera 节点允许落后的最大事务数 |
| `comment` | 管理员备注 |

`writer_is_also_reader` 的页面解释为：

- `0`：Writer 不作为 Reader
- `1`：Writer 也作为 Reader
- `2`：仅 Backup Writer 也作为 Reader

## 4. 成员角色推断

同一层级的 `mysql_servers.hostgroup_id` 按定义表映射为 Writer、Backup Writer、Reader 或
Offline。一个 ID 同时被多个角色引用时显示“角色冲突”；定义存在但没有成员时仍显示“未配置成员”。

页面不会因为同一个物理后端在不同主机组出现而误报冲突，这种情况可能是 Galera Monitor 的合法调度结果。
成员表显示 `mysql_servers.comment`，Main 层同时显示操作列。定义为空时成员列表必须为空，不能把
其他 Hostgroup 的服务器标记为 Galera 成员；定义引用的 Hostgroup 没有服务器时显示“未配置成员”。

## 5. 当前实例说明

当前就地部署实例实际配置的是 Galera：`mysql_galera_hostgroups` 有配置，而
`mysql_replication_hostgroups` 为空。因此查看当前拓扑应使用 Galera 页面，不应向传统
Replication 表追加 `(writer_hostgroup, reader_hostgroup)` 定义。

所有写入拓扑的操作都必须在页面之外制定迁移、验证和回滚方案，并取得明确确认。
