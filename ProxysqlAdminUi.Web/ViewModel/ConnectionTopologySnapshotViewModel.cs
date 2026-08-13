using ProxysqlAdminUi.Web.Models;

namespace ProxysqlAdminUi.Web.ViewModel;

public sealed class ConnectionTopologySnapshotViewModel
{
    public IReadOnlyDictionary<string, string> GlobalStats { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<StatsMySqlConnectionPoolModel> ConnectionPool { get; init; } = [];
    public IReadOnlyList<MysqlServerModel> RuntimeServers { get; init; } = [];
    public IReadOnlyDictionary<int, IReadOnlyList<HostgroupRole>> HostgroupRoles { get; init; } =
        new Dictionary<int, IReadOnlyList<HostgroupRole>>();
    public DateTimeOffset CollectedAt { get; init; }
}
