using ProxysqlAdminUi.Web.Models;

namespace ProxysqlAdminUi.Web.ViewModel;

public sealed class GaleraHostgroupOverviewViewModel
{
    public IReadOnlyList<MysqlGaleraHostgroupModel> Definitions { get; init; } = [];
    public IReadOnlyList<HostgroupMemberViewModel> Members { get; init; } = [];

    public static GaleraHostgroupOverviewViewModel Create(
        IEnumerable<MysqlGaleraHostgroupModel> definitions,
        IEnumerable<MysqlServerModel> servers)
    {
        var definitionList = definitions.ToList();
        var serverList = servers.ToList();
        var rolesByHostgroup = new Dictionary<int, HashSet<HostgroupRole>>();

        foreach (var definition in definitionList)
        {
            AddRole(rolesByHostgroup, definition.WriterHostgroup, HostgroupRole.Writer);
            AddRole(rolesByHostgroup, definition.BackupWriterHostgroup, HostgroupRole.BackupWriter);
            AddRole(rolesByHostgroup, definition.ReaderHostgroup, HostgroupRole.Reader);
            AddRole(rolesByHostgroup, definition.OfflineHostgroup, HostgroupRole.Offline);
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

        return new GaleraHostgroupOverviewViewModel
        {
            Definitions = definitionList,
            Members = members
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
