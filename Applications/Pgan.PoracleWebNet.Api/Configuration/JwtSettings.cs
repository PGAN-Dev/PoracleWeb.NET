namespace Pgan.PoracleWebNet.Api.Configuration;

public class JwtSettings
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "Pgan.PoracleWebNet";
    public string Audience { get; set; } = "Pgan.PoracleWebNet.App";
    public int ExpirationMinutes { get; set; } = 1440; // 24 hours
}
