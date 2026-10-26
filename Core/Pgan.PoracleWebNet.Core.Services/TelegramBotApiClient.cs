using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// <c>getChatMember</c> with the bot token PoracleWeb.NET already holds for Telegram sign-in. The token is part of the
/// request path, so nothing here logs a URI, an exception message or a response body: a failure is logged by its
/// kind alone.
/// </summary>
public partial class TelegramBotApiClient(HttpClient httpClient, IConfiguration configuration, ILogger<TelegramBotApiClient> logger)
    : ITelegramMembershipClient
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<TelegramBotApiClient> _logger = logger;
    private readonly string _botToken = (configuration["Telegram:BotToken"] ?? string.Empty).Trim();

    public async Task<bool> IsMemberAsync(long chatId, long userId, CancellationToken cancellationToken = default)
    {
        if (this._botToken.Length == 0)
        {
            LogNoToken(this._logger);
            return false;
        }

        try
        {
            var path = string.Create(CultureInfo.InvariantCulture,
                $"bot{this._botToken}/getChatMember?chat_id={chatId}&user_id={userId}");
            using var response = await this._httpClient.GetAsync(path, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // 400 is the usual "user not found" or "chat not found", and 403 is a bot that is not in the group.
                LogRefused(this._logger, (int)response.StatusCode, chatId);
                return false;
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True
                || !root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object
                || !result.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String)
            {
                LogUnreadable(this._logger, chatId);
                return false;
            }

            switch (status.GetString())
            {
                case "member":
                case "administrator":
                case "creator":
                    return true;
                case "restricted":
                    // A restricted user can still be in the chat; is_member says which. PoracleNG ignores the flag.
                    return !(result.TryGetProperty("is_member", out var inChat) && inChat.ValueKind == JsonValueKind.False);
                default:
                    return false;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailed(this._logger, ex.GetType().Name, chatId);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram:BotToken is not set, so no one can be checked against Telegram's registration groups")]
    private static partial void LogNoToken(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Telegram answered {Status} for getChatMember on group {ChatId}; treating the person as not a member")]
    private static partial void LogRefused(ILogger logger, int status, long chatId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Telegram's getChatMember answer for group {ChatId} was not in the expected shape; treating the person as not a member")]
    private static partial void LogUnreadable(ILogger logger, long chatId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "getChatMember for group {ChatId} failed ({ExceptionType}); treating the person as not a member")]
    private static partial void LogFailed(ILogger logger, string exceptionType, long chatId);
}
