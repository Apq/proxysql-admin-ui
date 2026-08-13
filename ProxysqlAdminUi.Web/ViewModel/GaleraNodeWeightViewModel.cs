namespace ProxysqlAdminUi.Web.ViewModel;

public sealed class GaleraNodeWeightViewModel
{
    public string Hostname { get; init; } = string.Empty;
    public int Port { get; init; }
    public string ServerVersion { get; init; } = string.Empty;
    public string VersionComment { get; init; } = string.Empty;
    public string NodeName { get; init; } = string.Empty;
    public string ClusterName { get; init; } = string.Empty;
    public string ClusterStateUuid { get; init; } = string.Empty;
    public string ClusterStatus { get; init; } = string.Empty;
    public string LocalStateComment { get; init; } = string.Empty;
    public bool WsrepOn { get; init; }
    public bool Ready { get; init; }
    public bool Connected { get; init; }
    public int Weight { get; init; }
    public bool IsDefaultWeight { get; init; }
    public bool? HasWritePrivilege { get; init; }

    public bool IsHealthyForChange =>
        WsrepOn &&
        Ready &&
        Connected &&
        string.Equals(ClusterStatus, "Primary", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(LocalStateComment, "Synced", StringComparison.OrdinalIgnoreCase);
}

public sealed record GaleraWeightChangeRequest(
    int HostgroupId,
    string Hostname,
    int Port,
    string ExpectedNodeName,
    string ExpectedClusterName,
    string ExpectedClusterStateUuid,
    int ExpectedWeight,
    int RequestedWeight,
    string OperatorUsername);

public sealed record GaleraWeightChangeResult(
    GaleraNodeWeightViewModel Before,
    GaleraNodeWeightViewModel After);

public sealed record GaleraNodeWeightRowState(
    bool IsLoading,
    GaleraNodeWeightViewModel? Node,
    string? Error)
{
    public static GaleraNodeWeightRowState Loading { get; } = new(true, null, null);
}
