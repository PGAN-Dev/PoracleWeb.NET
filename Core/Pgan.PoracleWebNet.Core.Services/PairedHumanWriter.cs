using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public sealed partial class PairedHumanWriter(ILinkedAccount linkedAccount, ILogger<PairedHumanWriter> logger) : IPairedHumanWriter
{
    private readonly ILinkedAccount _linkedAccount = linkedAccount;
    private readonly ILogger<PairedHumanWriter> _logger = logger;

    public async Task<PairWriteResult<T>> WriteAsync<T>(string userId, Func<string, Task<T>> write)
    {
        var pair = await this._linkedAccount.ResolveAsync(userId);
        var primary = await write(userId);

        // A write to a paused side still lands: only a side with no human at all is skipped.
        var other = pair.IsLinked
            ? pair.ExistingHumanIds.FirstOrDefault(id => !string.Equals(id, userId, StringComparison.Ordinal))
            : null;
        if (other is null)
        {
            return new PairWriteResult<T>(primary, default, null);
        }

        try
        {
            return new PairWriteResult<T>(primary, await write(other), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var side = pair.SideOf(other) ?? LinkSide.Telegram;
            LogOtherSideFailed(this._logger, ex, other, side);
            return new PairWriteResult<T>(primary, default, new PairWarning(side, ReasonFor(ex, side)));
        }
    }

    public async Task<PairWriteResult<bool>> WriteAsync(string userId, Func<string, Task> write) =>
        await this.WriteAsync(userId, async id =>
        {
            await write(id);
            return true;
        });

    /// <summary>
    /// What the client may be told. Only a refusal that carries a sentence written for people is passed on
    /// (PoracleNG's own detail, or one of ours); anything else, an <see cref="HttpRequestException"/> naming a
    /// host and port included, becomes a fixed line, and the exception goes to the log.
    /// </summary>
    private static string ReasonFor(Exception ex, LinkSide side) =>
        ex is PoracleRequestRefusedException or PairWriteRefusedException && !string.IsNullOrWhiteSpace(ex.Message)
            ? ex.Message
            : $"Couldn't reach Poracle for {AccountLinkKeys.Wire(side)}.";

    public async Task<PairSide?> FindSideAsync(string userId, string humanId)
    {
        var pair = await this._linkedAccount.ResolveAsync(userId);
        return pair.Discord?.HumanId == humanId ? pair.Discord : pair.Telegram?.HumanId == humanId ? pair.Telegram : null;
    }

    public async Task<LinkSide?> SideOfAsync(string userId, string humanId) =>
        (await this._linkedAccount.ResolveAsync(userId)).SideOf(humanId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The linked account {HumanId} ({Side}) did not take a write the signed-in side did")]
    private static partial void LogOtherSideFailed(ILogger logger, Exception ex, string humanId, LinkSide side);
}
