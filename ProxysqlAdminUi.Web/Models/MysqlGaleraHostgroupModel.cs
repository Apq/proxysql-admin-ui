using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProxysqlAdminUi.Web.Models;

[Table("mysql_galera_hostgroups")]
public class MysqlGaleraHostgroupModel
{
    [Column("writer_hostgroup")]
    public int WriterHostgroup { get; set; }

    [Column("backup_writer_hostgroup")]
    public int BackupWriterHostgroup { get; set; }

    [Column("reader_hostgroup")]
    public int ReaderHostgroup { get; set; }

    [Column("offline_hostgroup")]
    public int OfflineHostgroup { get; set; }

    [Column("active")]
    public int Active { get; set; }

    [Column("max_writers")]
    public int MaxWriters { get; set; }

    [Column("writer_is_also_reader")]
    public int WriterIsAlsoReader { get; set; }

    [Column("max_transactions_behind")]
    public int MaxTransactionsBehind { get; set; }

    [Column("comment")]
    [MaxLength(255)]
    public string? Comment { get; set; }
}
