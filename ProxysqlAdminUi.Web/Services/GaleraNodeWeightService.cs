using System.Collections.Concurrent;
using MySql.Data.MySqlClient;
using ProxysqlAdminUi.Web.Models;
using ProxysqlAdminUi.Web.Repositories;
using ProxysqlAdminUi.Web.ViewModel;

namespace ProxysqlAdminUi.Web.Services;

public sealed class GaleraNodeWeightService(
    IConfiguration configuration,
    ProxySqlRepository proxySqlRepository,
    ILogger<GaleraNodeWeightService> logger)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EndpointLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private const string VariablesSql = """
        SHOW GLOBAL VARIABLES WHERE Variable_name IN (
            'wsrep_on',
            'wsrep_node_name',
            'wsrep_cluster_name',
            'wsrep_provider_options'
        )
        """;

    private const string StatusSql = """
        SHOW GLOBAL STATUS WHERE Variable_name IN (
            'wsrep_cluster_state_uuid',
            'wsrep_cluster_status',
            'wsrep_local_state_comment',
            'wsrep_ready',
            'wsrep_connected'
        )
        """;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(configuration["GALERA_USERNAME"]) &&
        !string.IsNullOrWhiteSpace(configuration["GALERA_PASSWORD"]);

    public async Task<GaleraNodeWeightViewModel> InspectAsync(
        int hostgroupId,
        string hostname,
        int port,
        CancellationToken cancellationToken = default)
    {
        await EnsureRuntimeGaleraMemberAsync(hostgroupId, hostname, port);
        return await InspectNodeAsync(hostname, port, cancellationToken);
    }

    public async Task<GaleraWeightChangeResult> ChangeWeightAsync(
        GaleraWeightChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.RequestedWeight is < 0 or > 255)
        {
            throw new InvalidOperationException("Galera quorum weight must be between 0 and 255.");
        }

        await EnsureRuntimeGaleraMemberAsync(request.HostgroupId, request.Hostname, request.Port);

        var endpointKey = $"{request.Hostname}:{request.Port}";
        var endpointLock = EndpointLocks.GetOrAdd(endpointKey, static _ => new SemaphoreSlim(1, 1));
        await endpointLock.WaitAsync(cancellationToken);
        try
        {
            var before = await InspectNodeAsync(request.Hostname, request.Port, cancellationToken);
            ValidateExpectedState(request, before);

            if (before.HasWritePrivilege is false)
            {
                throw new InvalidOperationException(
                    "The configured Galera node account does not have permission to change wsrep_provider_options.");
            }

            if (before.Weight == request.RequestedWeight)
            {
                throw new InvalidOperationException("The requested Galera quorum weight is already active.");
            }

            logger.LogInformation(
                "Galera quorum weight change requested by {Operator} for {Host}:{Port}, node {NodeName}, cluster {ClusterUuid}: {OldWeight} -> {NewWeight}",
                request.OperatorUsername,
                request.Hostname,
                request.Port,
                before.NodeName,
                before.ClusterStateUuid,
                before.Weight,
                request.RequestedWeight);

            await using (var connection = CreateConnection(request.Hostname, request.Port))
            {
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = "SET GLOBAL wsrep_provider_options = @providerOptions";
                command.Parameters.AddWithValue("@providerOptions", $"pc.weight={request.RequestedWeight}");
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            var after = await InspectNodeAsync(request.Hostname, request.Port, cancellationToken);
            if (after.Weight != request.RequestedWeight)
            {
                throw new InvalidOperationException(
                    $"The node reported quorum weight {after.Weight} after the change; expected {request.RequestedWeight}.");
            }

            if (!string.Equals(before.ClusterStateUuid, after.ClusterStateUuid, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The Galera cluster UUID changed while the quorum weight was being updated.");
            }

            logger.LogInformation(
                "Galera quorum weight change completed for {Host}:{Port}, node {NodeName}, cluster {ClusterUuid}: observed weight {Weight}",
                request.Hostname,
                request.Port,
                after.NodeName,
                after.ClusterStateUuid,
                after.Weight);

            return new GaleraWeightChangeResult(before, after);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Galera quorum weight change failed for {Host}:{Port}; requested by {Operator}",
                request.Hostname,
                request.Port,
                request.OperatorUsername);
            throw;
        }
        finally
        {
            endpointLock.Release();
        }
    }

    private async Task<GaleraNodeWeightViewModel> InspectNodeAsync(
        string hostname,
        int port,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        try
        {
            await using var connection = CreateConnection(hostname, port);
            await connection.OpenAsync(cancellationToken);

            var variables = await ReadNameValueRowsAsync(connection, VariablesSql, cancellationToken);
            var status = await ReadNameValueRowsAsync(connection, StatusSql, cancellationToken);
            var grants = await ReadGrantsAsync(connection, cancellationToken);
            var serverIdentity = await ReadServerIdentityAsync(connection, cancellationToken);

            if (!variables.ContainsKey("wsrep_on"))
            {
                throw new InvalidOperationException("The selected server does not expose Galera WSREP variables.");
            }

            var (weight, isDefaultWeight) = GaleraProviderOptionsParser.ParseWeight(
                variables.GetValueOrDefault("wsrep_provider_options"));

            return new GaleraNodeWeightViewModel
            {
                Hostname = hostname,
                Port = port,
                ServerVersion = serverIdentity.Version,
                VersionComment = serverIdentity.VersionComment,
                NodeName = variables.GetValueOrDefault("wsrep_node_name") ?? string.Empty,
                ClusterName = variables.GetValueOrDefault("wsrep_cluster_name") ?? string.Empty,
                ClusterStateUuid = status.GetValueOrDefault("wsrep_cluster_state_uuid") ?? string.Empty,
                ClusterStatus = status.GetValueOrDefault("wsrep_cluster_status") ?? string.Empty,
                LocalStateComment = status.GetValueOrDefault("wsrep_local_state_comment") ?? string.Empty,
                WsrepOn = ParseBoolean(variables.GetValueOrDefault("wsrep_on")),
                Ready = ParseBoolean(status.GetValueOrDefault("wsrep_ready")),
                Connected = ParseBoolean(status.GetValueOrDefault("wsrep_connected")),
                Weight = weight,
                IsDefaultWeight = isDefaultWeight,
                HasWritePrivilege = GetWritePrivilege(grants)
            };
        }
        catch (MySqlException ex)
        {
            logger.LogWarning(ex, "Galera node operation failed for {Host}:{Port}", hostname, port);
            throw new InvalidOperationException(
                $"Unable to access Galera node {hostname}:{port}: {GetSafeDatabaseError(ex)}",
                ex);
        }
    }

    private async Task EnsureRuntimeGaleraMemberAsync(int hostgroupId, string hostname, int port)
    {
        var overview = await proxySqlRepository.GetGaleraHostgroupOverview(ProxySqlConfigLayer.Runtime);
        var exists = overview.Members.Any(member =>
            member.HostgroupId == hostgroupId &&
            member.Server is not null &&
            member.Server.Port == port &&
            string.Equals(member.Server.Hostname, hostname, StringComparison.OrdinalIgnoreCase));

        if (!exists)
        {
            throw new InvalidOperationException(
                "The selected server is no longer a member of the Runtime Galera hostgroup topology.");
        }
    }

    private MySqlConnection CreateConnection(string hostname, int port)
    {
        EnsureConfigured();

        var builder = new MySqlConnectionStringBuilder
        {
            Server = hostname,
            Port = checked((uint)port),
            UserID = configuration["GALERA_USERNAME"],
            Password = configuration["GALERA_PASSWORD"],
            SslMode = MySqlSslMode.Preferred,
            ConnectionTimeout = 5,
            DefaultCommandTimeout = 10,
        };
        builder.Pooling = false;
        return new MySqlConnection(builder.ConnectionString);
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "Galera node credentials are not configured. Set PAI_GALERA_USERNAME and PAI_GALERA_PASSWORD.");
        }
    }

    private static void ValidateExpectedState(
        GaleraWeightChangeRequest request,
        GaleraNodeWeightViewModel current)
    {
        if (!string.Equals(request.ExpectedNodeName, current.NodeName, StringComparison.Ordinal) ||
            !string.Equals(request.ExpectedClusterName, current.ClusterName, StringComparison.Ordinal) ||
            !string.Equals(request.ExpectedClusterStateUuid, current.ClusterStateUuid, StringComparison.OrdinalIgnoreCase) ||
            request.ExpectedWeight != current.Weight)
        {
            throw new InvalidOperationException(
                "The Galera node identity, cluster UUID or quorum weight changed after the dialog was opened. Refresh and try again.");
        }
    }

    private static async Task<Dictionary<string, string>> ReadNameValueRowsAsync(
        MySqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
        {
            values[reader.GetString(0)] = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        }

        return values;
    }

    private static async Task<IReadOnlyList<string>> ReadGrantsAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SHOW GRANTS FOR CURRENT_USER";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var grants = new List<string>();
            while (await reader.ReadAsync(cancellationToken))
            {
                grants.Add(reader.GetString(0));
            }

            return grants;
        }
        catch (MySqlException)
        {
            // Some valid management accounts cannot inspect their own grants. The
            // write command remains the authoritative permission check.
            return [];
        }
    }

    private static async Task<(string Version, string VersionComment)> ReadServerIdentityAsync(
        MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @@version, @@version_comment";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return (string.Empty, string.Empty);
        }

        return (
            reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
            reader.IsDBNull(1) ? string.Empty : reader.GetString(1));
    }

    private static bool? GetWritePrivilege(IEnumerable<string> grants)
    {
        var grantText = string.Join('\n', grants);
        if (grantText.Contains("ALL PRIVILEGES ON *.*", StringComparison.OrdinalIgnoreCase) ||
            grantText.Contains("SUPER", StringComparison.OrdinalIgnoreCase) ||
            grantText.Contains("SYSTEM_VARIABLES_ADMIN", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return null;
    }

    private static bool ParseBoolean(string? value) =>
        value is not null &&
        (value.Equals("ON", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("YES", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("TRUE", StringComparison.OrdinalIgnoreCase));

    private static string GetSafeDatabaseError(MySqlException exception) => exception.Number switch
    {
        1045 => "authentication failed for the configured account.",
        1044 => "the configured account does not have access.",
        2002 or 2003 => "the node is unreachable.",
        _ => "the database rejected the operation."
    };
}
