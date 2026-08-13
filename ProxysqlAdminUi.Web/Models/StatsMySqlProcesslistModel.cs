using Microsoft.EntityFrameworkCore;

namespace ProxysqlAdminUi.Web.Models;

[Keyless]
public sealed class StatsMySqlProcesslistModel
{
    public long ThreadId { get; set; }
    public long SessionId { get; set; }
    public string User { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public string ClientHost { get; set; } = string.Empty;
    public int ClientPort { get; set; }
    public int Hostgroup { get; set; }
    public string LocalServerHost { get; set; } = string.Empty;
    public int LocalServerPort { get; set; }
    public string ServerHost { get; set; } = string.Empty;
    public int ServerPort { get; set; }
    public string Command { get; set; } = string.Empty;
    public long TimeMs { get; set; }
    public long StatusFlags { get; set; }
    public string Info { get; set; } = string.Empty;
}
