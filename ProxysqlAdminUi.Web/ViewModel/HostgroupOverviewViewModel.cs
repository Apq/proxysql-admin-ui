using ProxysqlAdminUi.Web.Models;

namespace ProxysqlAdminUi.Web.ViewModel;

public enum HostgroupRole
{
    Undefined,
    Writer,
    BackupWriter,
    Reader,
    Offline,
    Conflict
}

public sealed class HostgroupMemberViewModel
{
    public int HostgroupId { get; init; }
    public HostgroupRole Role { get; init; }
    public IReadOnlyList<HostgroupRole> AssignedRoles { get; init; } = [];
    public MysqlServerModel? Server { get; init; }
    public bool HasMember => Server is not null;
    public string Hostname => Server?.Hostname ?? string.Empty;
    public int? Port => Server?.Port;
    public string? Status => Server?.Status;
    public int? Weight => Server?.Weight;
    public int? MaxConnections => Server?.MaxConnections;
    public int? MaxReplicationLag => Server?.MaxReplicationLag;
    public string? Comment => Server?.Comment;
}

public sealed class HostgroupOverviewViewModel
{
    public IReadOnlyList<MysqlReplicationHostgroupModel> Definitions { get; init; } = [];
    public IReadOnlyList<HostgroupMemberViewModel> Members { get; init; } = [];
    public bool? WriterIsAlsoReader { get; init; }

    public static HostgroupOverviewViewModel Create(
        IEnumerable<MysqlReplicationHostgroupModel> definitions,
        IEnumerable<MysqlServerModel> servers,
        bool? writerIsAlsoReader)
    {
        var definitionList = definitions.ToList();
        var serverList = servers.ToList();
        var rolesByHostgroup = new Dictionary<int, HashSet<HostgroupRole>>();

        foreach (var definition in definitionList)
        {
            AddRole(rolesByHostgroup, definition.WriterHostgroup, HostgroupRole.Writer);
            AddRole(rolesByHostgroup, definition.ReaderHostgroup, HostgroupRole.Reader);
        }

        var hostgroupIds = rolesByHostgroup.Keys
            .OrderBy(hostgroupId => hostgroupId)
            .ToList();

        var members = new List<HostgroupMemberViewModel>();
        foreach (var hostgroupId in hostgroupIds)
        {
            var assignedRoles = rolesByHostgroup.GetValueOrDefault(hostgroupId)?
                .OrderBy(role => role)
                .ToArray() ?? [];
            var role = assignedRoles.Length switch
            {
                0 => HostgroupRole.Undefined,
                1 => assignedRoles[0],
                _ => HostgroupRole.Conflict
            };
            var hostgroupServers = serverList
                .Where(server => server.HostgroupId == hostgroupId)
                .OrderBy(server => server.Hostname)
                .ThenBy(server => server.Port)
                .ToList();

            if (hostgroupServers.Count == 0)
            {
                members.Add(new HostgroupMemberViewModel
                {
                    HostgroupId = hostgroupId,
                    Role = role,
                    AssignedRoles = assignedRoles
                });
                continue;
            }

            members.AddRange(hostgroupServers.Select(server => new HostgroupMemberViewModel
            {
                HostgroupId = hostgroupId,
                Role = role,
                AssignedRoles = assignedRoles,
                Server = server
            }));
        }

        return new HostgroupOverviewViewModel
        {
            Definitions = definitionList,
            Members = members,
            WriterIsAlsoReader = writerIsAlsoReader
        };
    }

    private static void AddRole(
        IDictionary<int, HashSet<HostgroupRole>> rolesByHostgroup,
        int hostgroupId,
        HostgroupRole role)
    {
        if (!rolesByHostgroup.TryGetValue(hostgroupId, out var roles))
        {
            roles = [];
            rolesByHostgroup[hostgroupId] = roles;
        }

        roles.Add(role);
    }
}
