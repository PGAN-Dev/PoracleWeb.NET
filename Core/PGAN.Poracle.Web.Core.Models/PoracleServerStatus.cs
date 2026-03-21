namespace PGAN.Poracle.Web.Core.Models;

public class PoracleServerStatus
{
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public bool Online { get; set; }
    public string? Message { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}
