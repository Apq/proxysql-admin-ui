using System.Data;
using Microsoft.EntityFrameworkCore;
using ProxysqlAdminUi.Web.Contexts;
using ProxysqlAdminUi.Web.Models;
using ProxysqlAdminUi.Web.ViewModel;

namespace ProxysqlAdminUi.Web.Repositories;

public class ProxySqlRepository(IDbContextFactory<ProxySqlContext> dbContextFactory)
{
    // MySQL Servers
    public async Task<IEnumerable<MysqlServerModel>> GetMySqlServers()
    {
        await using var context = await CreateContextAsync();
        return await context.MySqlServers
            .FromSqlRaw("SELECT * FROM mysql_servers ORDER BY hostgroup_id, hostname, port")
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

    // MySQL Users
    public async Task<IEnumerable<MysqlUserModel>> GetMySqlUsers()
    {
        await using var context = await CreateContextAsync();
        return await context.MySqlUsers
            .FromSqlRaw("SELECT * FROM mysql_users")
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
    public async Task<IEnumerable<MysqlQueryRuleModel>> GetMySqlQueryRules()
    {
        await using var context = await CreateContextAsync();
        return await context.MySqlQueryRules
            .FromSqlRaw("SELECT * FROM mysql_query_rules")
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<IEnumerable<QueryRuleViewModel>> GetQueryRulesWithStats()
    {
        await using var context = await CreateContextAsync();
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
    public async Task<IEnumerable<GlobalVariableModel>> GetGlobalVariables()
    {
        await using var context = await CreateContextAsync();
        return await context.GlobalVariables
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<int> UpdateGlobalVariable(GlobalVariableModel variable)
    {
        await using var context = await CreateContextAsync();
        var result = await context.Database.ExecuteSqlRawAsync(
            "UPDATE global_variables SET variable_value = {0} WHERE variable_name = {1}",
            variable.VariableValue, variable.VariableName);

        await context.Database.ExecuteSqlRawAsync("LOAD ADMIN VARIABLES TO RUNTIME");
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
