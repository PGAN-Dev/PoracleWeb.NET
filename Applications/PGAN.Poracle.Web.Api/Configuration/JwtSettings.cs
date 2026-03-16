namespace PGAN.Poracle.Web.Api.Configuration;

public class JwtSettings
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "PGAN.Poracle.Web";
    public string Audience { get; set; } = "PGAN.Poracle.Web.App";
    public int ExpirationMinutes { get; set; } = 1440; // 24 hours
}
