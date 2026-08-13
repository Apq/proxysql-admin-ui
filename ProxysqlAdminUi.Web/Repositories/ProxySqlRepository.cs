using System.Data;
using Microsoft.EntityFrameworkCore;
using ProxysqlAdminUi.Web.Contexts;
using ProxysqlAdminUi.Web.Models;
using ProxysqlAdminUi.Web.ViewModel;

namespace ProxysqlAdminUi.Web.Repositories;

public class ProxySqlRepository(IDbContextFactory<ProxySqlContext> dbContextFactory)
{
    private static readonly IReadOnlyDictionary<(ProxySqlConfigLayer Layer, ProxySqlConfigTable Table), string>
        ConfigTableMap = new Dictionary<(ProxySqlConfigLayer, ProxySqlConfigTable), string>
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
            [(ProxySqlConfigLayer.Disk, ProxySqlConfigTable.GlobalVariables)] = "disk.global_variables",
            [(ProxySqlConfigLayer.Main, ProxySqlConfigTable.MysqlReplicationHostgroups)] = "mysql_replication_hostgroups",
            [(ProxySqlConfigLayer.Runtime, ProxySqlConfigTable.MysqlReplicationHostgroups)] = "runtime_mysql_replication_hostgroups",
            [(ProxySqlConfigLayer.Disk, ProxySqlConfigTable.MysqlReplicationHostgroups)] = "disk.mysql_replication_hostgroups",
            [(ProxySqlConfigLayer.Main, ProxySqlConfigTable.MysqlGaleraHostgroups)] = "mysql_galera_hostgroups",
            [(ProxySqlConfigLayer.Runtime, ProxySqlConfigTable.MysqlGaleraHostgroups)] = "runtime_mysql_galera_hostgroups",
            [(ProxySqlConfigLayer.Disk, ProxySqlConfigTable.MysqlGaleraHostgroups)] = "disk.mysql_galera_hostgroups"
        };

    // MySQL Servers
    public async Task<IEnumerable<MysqlServerModel>> GetMySqlServers(
        ProxySqlConfigLayer layer = ProxySqlConfigLayer.Main)
    {
        await using var context = await CreateContextAsync();
        var table = GetConfigTableName(layer, ProxySqlConfigTable.MysqlServers);
        var sql = $"SELECT * FROM {table} ORDER BY hostgroup_id, hostname, port";
        return await context.MySqlServers
            .FromSqlRaw(sql)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<MysqlServerModel?> GetMySqlServer(int hostgroupId, string hostname, int port)
    {
        await using var context = await CreateContextAsync();
        return await context.MySqlServers
            .FromSqlRaw(
                "SELECT * FROM mysql_servers WHERE hostgroup_id = {0} AND hostname = {1} AND port = {2}",
                hostgroupId, hostname, port)
            .AsNoTracking()
            .SingleOrDefaultAsync();
    }

    public async Task<int> AddMySqlServer(MysqlServerModel server)
    {
        await using var context = await CreateContextAsync();
        const string sql = @"INSERT INTO mysql_servers
            (hostgroup_id, hostname, port, gtid_port, status, weight, compression,
             max_connections, max_replication_lag, use_ssl, max_latency_ms, comment)
            VALUES
            ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11})";

        var result = await context.Database.ExecuteSqlRawAsync(sql,
            server.HostgroupId, server.Hostname, server.Port, server.GtidPort,
            server.Status, server.Weight, server.Compression, server.MaxConnections,
            server.MaxReplicationLag, server.UseSsl, server.MaxLatencyMs, server.Comment);

        await ApplyMySqlServersAsync(context);
        return result;
    }

    public async Task<int> UpdateMySqlServer(
        int originalHostgroupId,
        string originalHostname,
        int originalPort,
        MysqlServerModel server)
    {
        await using var context = await CreateContextAsync();
        const string sql = @"UPDATE mysql_servers
            SET hostgroup_id = {3}, hostname = {4}, port = {5}, gtid_port = {6},
                status = {7}, weight = {8}, compression = {9},
                max_connections = {10}, max_replication_lag = {11},
                use_ssl = {12}, max_latency_ms = {13}, comment = {14}
            WHERE hostgroup_id = {0} AND hostname = {1} AND port = {2}";

        var result = await context.Database.ExecuteSqlRawAsync(sql,
            originalHostgroupId, originalHostname, originalPort,
            server.HostgroupId, server.Hostname, server.Port, server.GtidPort,
            server.Status, server.Weight, server.Compression, server.MaxConnections,
            server.MaxReplicationLag, server.UseSsl, server.MaxLatencyMs, server.Comment);

        // MySQL can report zero affected rows when all submitted values are unchanged.
        // LOAD/SAVE must still run so the explicit user action remains idempotent.
        await ApplyMySqlServersAsync(context);
        return result;
    }

    public async Task<int> DeleteMySqlServer(int hostgroupId, string hostname, int port)
    {
        await using var context = await CreateContextAsync();
        var result = await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM mysql_servers WHERE hostgroup_id = {0} AND hostname = {1} AND port = {2}",
            hostgroupId, hostname, port);

        EnsureRowChanged(result, "The MySQL server no longer exists or was changed by another user.");
        await ApplyMySqlServersAsync(context);
        return result;
    }

    // MySQL Replication Hostgroups
    public async Task<IReadOnlyList<MysqlReplicationHostgroupModel>> GetMySqlReplicationHostgroups(
        ProxySqlConfigLayer layer = ProxySqlConfigLayer.Main)
    {
        await using var context = await CreateContextAsync();
        var table = GetConfigTableName(layer, ProxySqlConfigTable.MysqlReplicationHostgroups);
        var sql = $"SELECT writer_hostgroup, reader_hostgroup, check_type, comment FROM {table} ORDER BY writer_hostgroup, reader_hostgroup";
        return await context.Database.SqlQueryRaw<MysqlReplicationHostgroupModel>(sql).ToListAsync();
    }

    public async Task<HostgroupOverviewViewModel> GetHostgroupOverview(
        ProxySqlConfigLayer layer = ProxySqlConfigLayer.Main)
    {
        var definitions = await GetMySqlReplicationHostgroups(layer);
        var servers = await GetMySqlServers(layer);
        bool? writerIsAlsoReader = null;
        try
        {
            var variables = await GetGlobalVariables(layer);
            var writerIsAlsoReaderValue = variables
                .FirstOrDefault(variable => string.Equals(
                    variable.VariableName,
                    "mysql-monitor_writer_is_also_reader",
                    StringComparison.OrdinalIgnoreCase))?.VariableValue;
            writerIsAlsoReader = ParseProxySqlBoolean(writerIsAlsoReaderValue);
        }
        catch
        {
            // Hostgroup definitions and members remain useful when global variables are unavailable.
        }

        return HostgroupOverviewViewModel.Create(definitions, servers, writerIsAlsoReader);
    }

    // MySQL Galera Hostgroups
    public async Task<IReadOnlyList<MysqlGaleraHostgroupModel>> GetMySqlGaleraHostgroups(
        ProxySqlConfigLayer layer = ProxySqlConfigLayer.Main)
    {
        await using var context = await CreateContextAsync();
        var table = GetConfigTableName(layer, ProxySqlConfigTable.MysqlGaleraHostgroups);
        var sql = $@"SELECT writer_hostgroup, backup_writer_hostgroup, reader_hostgroup,
                           offline_hostgroup, active, max_writers, writer_is_also_reader,
                           max_transactions_behind, comment
                    FROM {table}
                    ORDER BY writer_hostgroup, backup_writer_hostgroup, reader_hostgroup, offline_hostgroup";
        return await context.Database.SqlQueryRaw<MysqlGaleraHostgroupModel>(sql).ToListAsync();
    }

    public async Task<GaleraHostgroupOverviewViewModel> GetGaleraHostgroupOverview(
        ProxySqlConfigLayer layer = ProxySqlConfigLayer.Main)
    {
        var definitions = await GetMySqlGaleraHostgroups(layer);
        var servers = await GetMySqlServers(layer);
        return GaleraHostgroupOverviewViewModel.Create(definitions, servers);
    }

    // MySQL Users
    public async Task<IEnumerable<MysqlUserModel>> GetMySqlUsers(
        ProxySqlConfigLayer layer = ProxySqlConfigLayer.Main)
    {
        await using var context = await CreateContextAsync();
        var table = GetConfigTableName(layer, ProxySqlConfigTable.MysqlUsers);
        var sql = $"SELECT * FROM {table} ORDER BY username, backend";
        return await context.MySqlUsers
            .FromSqlRaw(sql)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<MysqlUserModel?> GetMySqlUser(string username, int backend)
    {
        await using var context = await CreateContextAsync();
        return await context.MySqlUsers
            .FromSqlRaw(
                "SELECT * FROM mysql_users WHERE username = {0} AND backend = {1}",
                username, backend)
            .AsNoTracking()
            .SingleOrDefaultAsync();
    }

    public async Task<MysqlUserModel> AddMySqlUser(MysqlUserModel user)
    {
        await using var context = await CreateContextAsync();
        const string sql = @"INSERT INTO mysql_users
            (username, password, active, use_ssl, default_hostgroup, default_schema,
             schema_locked, transaction_persistent, fast_forward, backend, frontend,
             max_connections, attributes, comment)
            VALUES
            ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12}, {13})";

        await context.Database.ExecuteSqlRawAsync(sql,
            user.Username, user.Password, user.Active, user.UseSsl,
            user.DefaultHostgroup, user.DefaultSchema, user.SchemaLocked,
            user.TransactionPersistent, user.FastForward, user.Backend,
            user.Frontend, user.MaxConnections, user.Attributes, user.Comment);

        await ApplyMySqlUsersAsync(context);
        return user;
    }

    public async Task<int> UpdateMySqlUser(
        string originalUsername,
        int originalBackend,
        MysqlUserModel user)
    {
        await using var context = await CreateContextAsync();
        const string sql = @"UPDATE mysql_users
            SET username = {2}, backend = {3}, password = {4}, active = {5},
                use_ssl = {6}, default_hostgroup = {7}, default_schema = {8},
                schema_locked = {9}, transaction_persistent = {10},
                fast_forward = {11}, frontend = {12}, max_connections = {13},
                attributes = {14}, comment = {15}
            WHERE username = {0} AND backend = {1}";

        var result = await context.Database.ExecuteSqlRawAsync(sql,
            originalUsername, originalBackend, user.Username, user.Backend,
            user.Password, user.Active, user.UseSsl, user.DefaultHostgroup,
            user.DefaultSchema, user.SchemaLocked, user.TransactionPersistent,
            user.FastForward, user.Frontend, user.MaxConnections,
            user.Attributes, user.Comment);

        // MySQL can report zero affected rows when all submitted values are unchanged.
        await ApplyMySqlUsersAsync(context);
        return result;
    }

    public async Task<int> DeleteMySqlUser(string username, int backend)
    {
        await using var context = await CreateContextAsync();
        var result = await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM mysql_users WHERE username = {0} AND backend = {1}",
            username, backend);

        EnsureRowChanged(result, "The MySQL user no longer exists or was changed by another user.");
        await ApplyMySqlUsersAsync(context);
        return result;
    }

    // MySQL Query Rules
    public async Task<IEnumerable<MysqlQueryRuleModel>> GetMySqlQueryRules(
        ProxySqlConfigLayer layer = ProxySqlConfigLayer.Main)
    {
        await using var context = await CreateContextAsync();
        var table = GetConfigTableName(layer, ProxySqlConfigTable.MysqlQueryRules);
        var sql = $"SELECT * FROM {table} ORDER BY rule_id";
        return await context.MySqlQueryRules
            .FromSqlRaw(sql)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<IEnumerable<QueryRuleViewModel>> GetQueryRulesWithStats(
        ProxySqlConfigLayer layer = ProxySqlConfigLayer.Main)
    {
        await using var context = await CreateContextAsync();
        if (layer != ProxySqlConfigLayer.Main)
        {
            var table = GetConfigTableName(layer, ProxySqlConfigTable.MysqlQueryRules);
            var readOnlySql = $@"
SELECT r.*,
    0 AS Hits,
    NULL AS DigestText,
    0 AS CountStar
FROM {table} r
ORDER BY r.rule_id";

            return await context.Database.SqlQueryRaw<QueryRuleViewModel>(readOnlySql)
                .ToListAsync();
        }

        const string sql = @"
SELECT r.*,
    COALESCE(s.hits, 0) as Hits,
    MAX(d.digest_text) as DigestText,
    CASE WHEN d.hostgroup >= 0 THEN d.count_star ELSE 0 END as CountStar
FROM mysql_query_rules r
LEFT JOIN stats_mysql_query_rules s ON r.rule_id = s.rule_id
LEFT JOIN stats_mysql_query_digest d ON r.digest = d.digest
GROUP BY r.rule_id, r.active, r.username, r.schemaname, r.flagIN, r.client_addr,
         r.proxy_addr, r.proxy_port, r.digest, r.match_digest, r.match_pattern,
         r.negate_match_pattern, r.re_modifiers, r.flagOUT, r.replace_pattern,
         r.destination_hostgroup, r.cache_ttl, r.cache_empty_result, r.cache_timeout,
         r.reconnect, r.timeout, r.retries, r.delay, r.next_query_flagIN,
         r.mirror_flagOUT, r.mirror_hostgroup, r.error_msg, r.OK_msg, r.sticky_conn,
         r.multiplex, r.gtid_from_hostgroup, r.log, r.apply, r.attributes, r.comment,
         s.hits";

        return await context.Database.SqlQueryRaw<QueryRuleViewModel>(sql)
            .ToListAsync();
    }

    public async Task<MysqlQueryRuleModel?> GetMySqlQueryRule(int ruleId)
    {
        await using var context = await CreateContextAsync();
        return await context.MySqlQueryRules
            .FromSqlRaw("SELECT * FROM mysql_query_rules WHERE rule_id = {0}", ruleId)
            .AsNoTracking()
            .FirstOrDefaultAsync();
    }

    public async Task<int> AddMySqlQueryRule(MysqlQueryRuleModel rule)
    {
        await using var context = await CreateContextAsync();
        const string sql = @"INSERT INTO mysql_query_rules
            (active, username, schemaname, flagIN, client_addr, proxy_addr,
             proxy_port, digest, match_digest, match_pattern, negate_match_pattern,
             re_modifiers, flagOUT, replace_pattern, destination_hostgroup,
             cache_ttl, cache_empty_result, cache_timeout, reconnect, timeout,
             retries, delay, next_query_flagIN, mirror_flagOUT, mirror_hostgroup,
             error_msg, OK_msg, sticky_conn, multiplex, gtid_from_hostgroup,
             log, apply, attributes, comment)
            VALUES
            ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11},
             {12}, {13}, {14}, {15}, {16}, {17}, {18}, {19}, {20}, {21},
             {22}, {23}, {24}, {25}, {26}, {27}, {28}, {29}, {30}, {31},
             {32}, {33})";

        var result = await context.Database.ExecuteSqlRawAsync(sql,
            ToDbValues(
                rule.Active, rule.Username, rule.Schemaname, rule.FlagIn,
                rule.ClientAddr, rule.ProxyAddr, rule.ProxyPort, rule.Digest,
                rule.MatchDigest, rule.MatchPattern, rule.NegateMatchPattern,
                rule.ReModifiers, rule.FlagOut, rule.ReplacePattern,
                rule.DestinationHostgroup, rule.CacheTtl, rule.CacheEmptyResult,
                rule.CacheTimeout, rule.Reconnect, rule.Timeout, rule.Retries,
                rule.Delay, rule.NextQueryFlagIn, rule.MirrorFlagOut,
                rule.MirrorHostgroup, rule.ErrorMsg, rule.OKMsg, rule.StickyConn,
                rule.Multiplex, rule.GtidFromHostgroup, rule.Log, rule.Apply,
                rule.Attributes, rule.Comment));

        await ApplyMySqlQueryRulesAsync(context);
        return result;
    }

    public async Task<int> UpdateMySqlQueryRule(MysqlQueryRuleModel rule)
    {
        await using var context = await CreateContextAsync();
        const string sql = @"UPDATE mysql_query_rules
            SET active = {1}, username = {2}, schemaname = {3},
                flagIN = {4}, client_addr = {5}, proxy_addr = {6},
                proxy_port = {7}, digest = {8}, match_digest = {9},
                match_pattern = {10}, negate_match_pattern = {11},
                re_modifiers = {12}, flagOUT = {13}, replace_pattern = {14},
                destination_hostgroup = {15}, cache_ttl = {16},
                cache_empty_result = {17}, cache_timeout = {18},
                reconnect = {19}, timeout = {20}, retries = {21},
                delay = {22}, next_query_flagIN = {23}, mirror_flagOUT = {24},
                mirror_hostgroup = {25}, error_msg = {26}, OK_msg = {27},
                sticky_conn = {28}, multiplex = {29}, gtid_from_hostgroup = {30},
                log = {31}, apply = {32}, attributes = {33}, comment = {34}
            WHERE rule_id = {0}";

        var result = await context.Database.ExecuteSqlRawAsync(sql,
            ToDbValues(
                rule.RuleId, rule.Active, rule.Username, rule.Schemaname,
                rule.FlagIn, rule.ClientAddr, rule.ProxyAddr, rule.ProxyPort,
                rule.Digest, rule.MatchDigest, rule.MatchPattern,
                rule.NegateMatchPattern, rule.ReModifiers, rule.FlagOut,
                rule.ReplacePattern, rule.DestinationHostgroup, rule.CacheTtl,
                rule.CacheEmptyResult, rule.CacheTimeout, rule.Reconnect,
                rule.Timeout, rule.Retries, rule.Delay, rule.NextQueryFlagIn,
                rule.MirrorFlagOut, rule.MirrorHostgroup, rule.ErrorMsg,
                rule.OKMsg, rule.StickyConn, rule.Multiplex,
                rule.GtidFromHostgroup, rule.Log, rule.Apply,
                rule.Attributes, rule.Comment));

        await ApplyMySqlQueryRulesAsync(context);
        return result;
    }

    public async Task<int> DeleteMySqlQueryRule(int ruleId)
    {
        await using var context = await CreateContextAsync();
        var result = await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM mysql_query_rules WHERE rule_id = {0}", ruleId);

        await ApplyMySqlQueryRulesAsync(context);
        return result;
    }

    // Stats
    public async Task<IEnumerable<QueryDigestViewModel>> GetStatsMySqlQueryDigests()
    {
        await using var context = await CreateContextAsync();
        const string sql = """
                           SELECT d.*,
                                  rd.count_star                                     as CacheHits,
                                  CASE WHEN r.rule_id IS NOT NULL THEN 1 ELSE 0 END as HasRule,
                                  r.rule_id                                         as RuleId
                           FROM stats_mysql_query_digest d
                                    LEFT JOIN stats_mysql_query_digest rd
                                              ON d.digest = rd.digest
                                                  and d.username = rd.username
                                                  and d.schemaname = rd.schemaname
                                                  and rd.hostgroup = -1
                                    left join mysql_query_rules r
                                              on r.digest = d.digest
                           where d.hostgroup = 0
                           order by d.count_star desc
                           """;

        return await context.Database.SqlQueryRaw<QueryDigestViewModel>(sql)
            .ToListAsync();
    }

    public async Task<IEnumerable<StatsMySqlQueryRuleModel>> GetStatsMySqlQueryRules()
    {
        await using var context = await CreateContextAsync();
        return await context.StatsMySqlQueryRules
            .FromSqlRaw("SELECT * FROM stats_mysql_query_rules")
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task LoadRulesToRuntime()
    {
        await using var context = await CreateContextAsync();
        await context.Database.ExecuteSqlRawAsync("LOAD MYSQL QUERY RULES TO RUNTIME;");
    }

    public async Task SaveRulesToDisk()
    {
        await using var context = await CreateContextAsync();
        await context.Database.ExecuteSqlRawAsync("SAVE MYSQL QUERY RULES TO DISK;");
    }

    // Global variables
    public async Task<IEnumerable<GlobalVariableModel>> GetGlobalVariables(
        ProxySqlConfigLayer layer = ProxySqlConfigLayer.Main)
    {
        await using var context = await CreateContextAsync();
        var table = GetConfigTableName(layer, ProxySqlConfigTable.GlobalVariables);
        var sql = $"SELECT * FROM {table} ORDER BY variable_name";
        return await context.GlobalVariables
            .FromSqlRaw(sql)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<int> UpdateGlobalVariable(GlobalVariableModel variable)
    {
        await using var context = await CreateContextAsync();
        var commands = GetGlobalVariableCommands(variable.VariableName);
        var result = await context.Database.ExecuteSqlRawAsync(
            "UPDATE global_variables SET variable_value = {0} WHERE variable_name = {1}",
            variable.VariableValue, variable.VariableName);

        await ApplyGlobalVariablesAsync(context, commands);
        return result;
    }

    public async Task<Dictionary<string, string>> GetMySqlGlobalStats()
    {
        await using var context = await CreateContextAsync();
        var stats = await context.StatsMySqlGlobals
            .AsNoTracking()
            .ToListAsync();

        return stats.ToDictionary(x => x.VariableName, x => x.VariableValue);
    }

    public async Task<Dictionary<string, long>> GetMemoryMetrics()
    {
        await using var context = await CreateContextAsync();
        var metrics = await context.StatsMemoryMetrics
            .FromSqlRaw("SELECT * FROM stats_memory_metrics")
            .AsNoTracking()
            .ToListAsync();

        return metrics.ToDictionary(x => x.VariableName, x => x.VariableValue);
    }

    public async Task<IReadOnlyList<StatsMySqlConnectionPoolModel>> GetMySqlConnectionPool(
        int? hostgroupId = null)
    {
        await using var context = await CreateContextAsync();
        var availableColumns = await GetTableColumnsAsync(context, "stats_mysql_connection_pool");
        var latencyExpression = availableColumns.Contains("Latency_ms")
            ? "Latency_ms"
            : availableColumns.Contains("Latency_us")
                ? "CAST(Latency_us / 1000 AS INTEGER)"
                : "0";
        var columns = $@"SELECT hostgroup AS Hostgroup,
                                        srv_host AS ServerHost,
                                        srv_port AS ServerPort,
                                        status AS Status,
                                        ConnUsed,
                                        ConnFree,
                                        ConnOK AS ConnOk,
                                        ConnERR AS ConnErr,
                                        Queries,
                                        Bytes_data_sent AS BytesDataSent,
                                        Bytes_data_recv AS BytesDataRecv,
                                        {latencyExpression} AS LatencyMs
                                 FROM stats_mysql_connection_pool";
        var sql = hostgroupId.HasValue
            ? $"{columns} WHERE hostgroup = {{0}} ORDER BY hostgroup, srv_host, srv_port"
            : $"{columns} ORDER BY hostgroup, srv_host, srv_port";

        return hostgroupId.HasValue
            ? await context.StatsMySqlConnectionPool.FromSqlRaw(sql, hostgroupId.Value).AsNoTracking().ToListAsync()
            : await context.StatsMySqlConnectionPool.FromSqlRaw(sql).AsNoTracking().ToListAsync();
    }

    private static async Task<HashSet<string>> GetTableColumnsAsync(
        ProxySqlContext context,
        string tableName)
    {
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {tableName} LIMIT 0";
            await using var reader = await command.ExecuteReaderAsync();
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                columns.Add(reader.GetName(index));
            }

            return columns;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<PagedResult<StatsMySqlProcesslistModel>> GetMySqlProcesslistPage(
        int skip,
        int pageSize,
        int? hostgroupId = null,
        string? sortProperty = null,
        bool sortDescending = true)
    {
        await using var context = await CreateContextAsync();
        var safeSkip = Math.Max(0, skip);
        var safePageSize = Math.Clamp(pageSize, 1, 200);
        var orderColumn = GetProcesslistOrderColumn(sortProperty);
        var orderDirection = sortDescending ? "DESC" : "ASC";
        var whereClause = hostgroupId.HasValue ? $" WHERE hostgroup = {hostgroupId.Value}" : string.Empty;

        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        int totalCount;
        try
        {
            await using var countCommand = connection.CreateCommand();
            countCommand.CommandText = $"SELECT COUNT(*) FROM stats_mysql_processlist{whereClause}";
            totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }

        const string columns = @"SELECT ThreadID AS ThreadId,
                                        SessionID AS SessionId,
                                        COALESCE(user, '') AS User,
                                        COALESCE(db, '') AS Database,
                                        COALESCE(cli_host, '') AS ClientHost,
                                        COALESCE(cli_port, 0) AS ClientPort,
                                        COALESCE(hostgroup, -1) AS Hostgroup,
                                        COALESCE(l_srv_host, '') AS LocalServerHost,
                                        COALESCE(l_srv_port, 0) AS LocalServerPort,
                                        COALESCE(srv_host, '') AS ServerHost,
                                        COALESCE(srv_port, 0) AS ServerPort,
                                        COALESCE(command, '') AS Command,
                                        COALESCE(time_ms, 0) AS TimeMs,
                                        COALESCE(status_flags, 0) AS StatusFlags,
                                        COALESCE(info, '') AS Info
                                 FROM stats_mysql_processlist";
        var sql = $"{columns}{whereClause} ORDER BY {orderColumn} {orderDirection} LIMIT {safePageSize} OFFSET {safeSkip}";
        var items = await context.StatsMySqlProcesslist
            .FromSqlRaw(sql)
            .AsNoTracking()
            .ToListAsync();

        return new PagedResult<StatsMySqlProcesslistModel>
        {
            Items = items,
            TotalCount = totalCount
        };
    }

    private static string GetProcesslistOrderColumn(string? property) => property switch
    {
        nameof(StatsMySqlProcesslistModel.ThreadId) => "ThreadID",
        nameof(StatsMySqlProcesslistModel.SessionId) => "SessionID",
        nameof(StatsMySqlProcesslistModel.User) => "user",
        nameof(StatsMySqlProcesslistModel.Database) => "db",
        nameof(StatsMySqlProcesslistModel.ClientHost) => "cli_host",
        nameof(StatsMySqlProcesslistModel.ClientPort) => "cli_port",
        nameof(StatsMySqlProcesslistModel.Hostgroup) => "hostgroup",
        nameof(StatsMySqlProcesslistModel.ServerHost) => "srv_host",
        nameof(StatsMySqlProcesslistModel.ServerPort) => "srv_port",
        nameof(StatsMySqlProcesslistModel.Command) => "command",
        nameof(StatsMySqlProcesslistModel.StatusFlags) => "status_flags",
        _ => "time_ms"
    };

    public async Task<ConnectionTopologySnapshotViewModel> GetConnectionTopologySnapshot(
        int? hostgroupId = null)
    {
        var globalStats = await GetMySqlGlobalStats();
        var connectionPool = await GetMySqlConnectionPool(hostgroupId);
        var runtimeServers = (await GetMySqlServers(ProxySqlConfigLayer.Runtime))
            .Where(server => !hostgroupId.HasValue || server.HostgroupId == hostgroupId.Value)
            .ToList();
        var hostgroupRoles = await GetRuntimeHostgroupRoles();

        return new ConnectionTopologySnapshotViewModel
        {
            GlobalStats = globalStats,
            ConnectionPool = connectionPool,
            RuntimeServers = runtimeServers,
            HostgroupRoles = hostgroupRoles,
            CollectedAt = DateTimeOffset.Now
        };
    }

    public async Task ResetStats()
    {
        await using var context = await CreateContextAsync();
        await context.Database.ExecuteSqlRawAsync("SELECT 1 FROM stats_mysql_query_digest_reset LIMIT 1;");
        await context.Database.ExecuteSqlRawAsync("LOAD MYSQL QUERY RULES TO RUNTIME;");
    }

    public async Task FlushQueryCache()
    {
        await using var context = await CreateContextAsync();
        await context.Database.ExecuteSqlRawAsync("PROXYSQL FLUSH QUERY CACHE;");
    }

    public async Task<ServerStatusSummaryViewModel> GetServerStatusSummary()
    {
        var servers = await GetMySqlServers();
        return new ServerStatusSummaryViewModel
        {
            Online = servers.Count(s => s.Status == "ONLINE"),
            Offline = servers.Count(s => s.Status.StartsWith("OFFLINE", StringComparison.OrdinalIgnoreCase)),
            Shunned = servers.Count(s => s.Status == "SHUNNED")
        };
    }

    private Task<ProxySqlContext> CreateContextAsync()
    {
        return dbContextFactory.CreateDbContextAsync();
    }

    private async Task<IReadOnlyDictionary<int, IReadOnlyList<HostgroupRole>>> GetRuntimeHostgroupRoles()
    {
        var roles = new Dictionary<int, HashSet<HostgroupRole>>();

        try
        {
            foreach (var definition in await GetMySqlReplicationHostgroups(ProxySqlConfigLayer.Runtime))
            {
                AddHostgroupRole(roles, definition.WriterHostgroup, HostgroupRole.Writer);
                AddHostgroupRole(roles, definition.ReaderHostgroup, HostgroupRole.Reader);
            }
        }
        catch
        {
            // A topology table can be unavailable on a supported ProxySQL deployment.
        }

        try
        {
            foreach (var definition in await GetMySqlGaleraHostgroups(ProxySqlConfigLayer.Runtime))
            {
                AddHostgroupRole(roles, definition.WriterHostgroup, HostgroupRole.Writer);
                AddHostgroupRole(roles, definition.BackupWriterHostgroup, HostgroupRole.BackupWriter);
                AddHostgroupRole(roles, definition.ReaderHostgroup, HostgroupRole.Reader);
                AddHostgroupRole(roles, definition.OfflineHostgroup, HostgroupRole.Offline);
            }
        }
        catch
        {
            // A topology table can be unavailable on a supported ProxySQL deployment.
        }

        return roles.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<HostgroupRole>)pair.Value.OrderBy(role => role).ToArray());
    }

    private static void AddHostgroupRole(
        IDictionary<int, HashSet<HostgroupRole>> roles,
        int hostgroupId,
        HostgroupRole role)
    {
        if (!roles.TryGetValue(hostgroupId, out var assignedRoles))
        {
            assignedRoles = [];
            roles[hostgroupId] = assignedRoles;
        }

        assignedRoles.Add(role);
    }

    private static async Task ApplyMySqlServersAsync(ProxySqlContext context)
    {
        await context.Database.ExecuteSqlRawAsync("LOAD MYSQL SERVERS TO RUNTIME;");
        await context.Database.ExecuteSqlRawAsync("SAVE MYSQL SERVERS TO DISK;");
    }

    private static async Task ApplyMySqlUsersAsync(ProxySqlContext context)
    {
        await context.Database.ExecuteSqlRawAsync("LOAD MYSQL USERS TO RUNTIME;");
        await context.Database.ExecuteSqlRawAsync("SAVE MYSQL USERS TO DISK;");
    }

    private static async Task ApplyMySqlQueryRulesAsync(ProxySqlContext context)
    {
        await context.Database.ExecuteSqlRawAsync("LOAD MYSQL QUERY RULES TO RUNTIME;");
        await context.Database.ExecuteSqlRawAsync("SAVE MYSQL QUERY RULES TO DISK;");
    }

    private static async Task ApplyGlobalVariablesAsync(
        ProxySqlContext context,
        (string Load, string Save) commands)
    {
        await context.Database.ExecuteSqlRawAsync(commands.Load);
        await context.Database.ExecuteSqlRawAsync(commands.Save);
    }

    private static (string Load, string Save) GetGlobalVariableCommands(string variableName)
    {
        if (variableName.StartsWith("admin-", StringComparison.OrdinalIgnoreCase))
        {
            return ("LOAD ADMIN VARIABLES TO RUNTIME;", "SAVE ADMIN VARIABLES TO DISK;");
        }

        if (variableName.StartsWith("mysql-", StringComparison.OrdinalIgnoreCase))
        {
            return ("LOAD MYSQL VARIABLES TO RUNTIME;", "SAVE MYSQL VARIABLES TO DISK;");
        }

        throw new NotSupportedException(
            $"Global variable '{variableName}' does not belong to a supported ADMIN or MYSQL variable group.");
    }

    public string GetConfigTableName(ProxySqlConfigLayer layer, ProxySqlConfigTable table)
    {
        return ConfigTableMap[(layer, table)];
    }

    private static bool? ParseProxySqlBoolean(string? value)
    {
        if (bool.TryParse(value, out var booleanValue))
        {
            return booleanValue;
        }

        if (int.TryParse(value, out var integerValue) && integerValue is 0 or 1)
        {
            return integerValue == 1;
        }

        return null;
    }

    private static object[] ToDbValues(params object?[] values)
    {
        return values.Select(static value => value ?? DBNull.Value).ToArray();
    }

    private static void EnsureRowChanged(int affectedRows, string message)
    {
        if (affectedRows == 0)
        {
            throw new InvalidOperationException(message);
        }
    }
}
