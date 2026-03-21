namespace PGAN.Poracle.Web.Api.Configuration;

public class PoracleServerConfig
{
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string ApiAddress { get; set; } = string.Empty;
    public string SshUser { get; set; } = "root";
}

public class PoracleServerSettings
{
    public List<PoracleServerConfig> Servers { get; set; } = [];
    public string SshKeyPath { get; set; } = "/app/ssh_key";
}
