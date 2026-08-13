using Microsoft.EntityFrameworkCore;

namespace ProxysqlAdminUi.Web.Models;

[Keyless]
public sealed class StatsMySqlConnectionPoolModel
{
    public int Hostgroup { get; set; }
    public string ServerHost { get; set; } = string.Empty;
    public int ServerPort { get; set; }
    public string Status { get; set; } = string.Empty;
    public long ConnUsed { get; set; }
    public long ConnFree { get; set; }
    public long ConnOk { get; set; }
    public long ConnErr { get; set; }
    public long Queries { get; set; }
    public long BytesDataSent { get; set; }
    public long BytesDataRecv { get; set; }
    public long LatencyMs { get; set; }
}
