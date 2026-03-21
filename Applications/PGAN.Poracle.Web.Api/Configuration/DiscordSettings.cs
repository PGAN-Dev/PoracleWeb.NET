namespace PGAN.Poracle.Web.Api.Configuration;

public class DiscordSettings
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string FrontendUrl { get; set; } = "http://localhost:4200";
    public string BotToken { get; set; } = string.Empty;
    public string GuildId { get; set; } = string.Empty;
    public string GeofenceForumChannelId { get; set; } = string.Empty;
}
