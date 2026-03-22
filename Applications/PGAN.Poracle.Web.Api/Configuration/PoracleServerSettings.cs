namespace PGAN.Poracle.Web.Api.Configuration;

public class PoracleServerConfig
{
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string ApiAddress { get; set; } = string.Empty;
    public string SshUser { get; set; } = "root";
    public string RestartCommand { get; set; } = "pm2 restart all";
    public string GroupMapPath { get; set; } = "/source/PoracleJS/config/group_map.json";
}

public class PoracleServerSettings
{
    public List<PoracleServerConfig> Servers { get; set; } = [];
    public string SshKeyPath { get; set; } = "/app/ssh_key";
}
