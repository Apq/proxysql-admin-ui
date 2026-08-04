using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProxysqlAdminUi.Web.Models;

[Table("mysql_replication_hostgroups")]
public class MysqlReplicationHostgroupModel
{
    [Column("writer_hostgroup")]
    public int WriterHostgroup { get; set; }

    [Column("reader_hostgroup")]
    public int ReaderHostgroup { get; set; }

    [Column("check_type")]
    [MaxLength(32)]
    public string? CheckType { get; set; }

    [Column("comment")]
    [MaxLength(255)]
    public string? Comment { get; set; }
}
