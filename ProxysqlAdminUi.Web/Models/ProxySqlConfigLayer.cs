namespace ProxysqlAdminUi.Web.Models;

public enum ProxySqlConfigLayer
{
    Main,
    Runtime,
    Disk
}

public enum ProxySqlConfigTable
{
    MysqlServers,
    MysqlUsers,
    MysqlQueryRules,
    GlobalVariables
}
