namespace Pgan.PoracleWebNet.Api.Configuration;

public class TelegramSettings
{
    public bool Enabled
    {
        get; set;
    }
    public string BotToken { get; set; } = string.Empty;
    public string BotUsername { get; set; } = string.Empty;
}
