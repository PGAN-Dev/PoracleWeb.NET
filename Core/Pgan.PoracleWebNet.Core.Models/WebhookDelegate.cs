namespace Pgan.PoracleWebNet.Core.Models;

public class WebhookDelegate
{
    public int Id
    {
        get; set;
    }
    public string WebhookId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime CreatedAt
    {
        get; set;
    }
}
