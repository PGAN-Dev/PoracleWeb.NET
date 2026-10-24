using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public class LinkedAccount(
    IAccountLinkRepository links,
    IHumanService humans,
    IFeatureGate featureGate,
    IMemoryCache cache,
    ILogger<LinkedAccount> logger) : ILinkedAccount
{
    private static readonly TimeSpan _ttl = TimeSpan.FromMinutes(1);

    private readonly IMemoryCache _cache = cache;
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IHumanService _humans = humans;
    private readonly IAccountLinkRepository _links = links;
    private readonly ILogger<LinkedAccount> _logger = logger;

    // Holders let a cached "no link" and a cached "no such human" be told apart from a cache miss.
    private sealed record LinkEntry(AccountLink? Link);

    private sealed record HumanEntry(Human? Human);

    public async Task<LinkedPair> ResolveAsync(string userId)
    {
        if (!await this._featureGate.IsEnabledAsync(DisableFeatureKeys.AccountLinking))
        {
            return LinkedPair.Single(userId);
        }

        var link = await this.GetLinkAsync(userId);
        if (link is null)
        {
            return LinkedPair.Single(userId);
        }

        var discord = await this.ResolveSideAsync(LinkSide.Discord, link.DiscordId, AccountLinkKeys.DiscordUserType);
        var telegram = await this.ResolveSideAsync(LinkSide.Telegram, link.TelegramId, AccountLinkKeys.TelegramUserType);
        return new LinkedPair(userId, link, discord, telegram);
    }

    public void Invalidate(string humanId)
    {
        var ids = new List<string> { humanId };
        if (this._cache.TryGetValue(LinkKey(humanId), out LinkEntry? entry) && entry?.Link is { } link)
        {
            ids.Add(link.DiscordId);
            ids.Add(link.TelegramId);
        }

        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            this._cache.Remove(LinkKey(id));
            this._cache.Remove(HumanKey(id));
        }
    }

    private static string LinkKey(string id) => $"account-link:{id}";

    private static string HumanKey(string id) => $"account-link-human:{id}";

    private async Task<AccountLink?> GetLinkAsync(string userId)
    {
        if (this._cache.TryGetValue(LinkKey(userId), out LinkEntry? cached) && cached is not null)
        {
            return cached.Link;
        }

        var link = await this._links.GetByHumanIdAsync(userId);
        this._cache.Set(LinkKey(userId), new LinkEntry(link), _ttl);
        return link;
    }

    private async Task<PairSide> ResolveSideAsync(LinkSide side, string humanId, string expectedType)
    {
        var key = HumanKey(humanId);
        if (!this._cache.TryGetValue(key, out HumanEntry? entry) || entry is null)
        {
            entry = new HumanEntry(await this._humans.GetByIdAsync(humanId));
            this._cache.Set(key, entry, _ttl);
            if (entry.Human is { } fresh && !string.Equals(fresh.Type, expectedType, StringComparison.Ordinal))
            {
                this._logger.LogWarning(
                    "Linked {Side} id {HumanId} is a {ActualType} human, not {ExpectedType}; treating that side as absent",
                    side, LogSafe.Id(humanId), fresh.Type, expectedType);
            }
        }

        var human = entry.Human;
        if (human is null || !string.Equals(human.Type, expectedType, StringComparison.Ordinal))
        {
            return new PairSide(side, humanId, false, PauseReason.NotRegistered);
        }

        var pause = human.AdminDisable == 1 ? PauseReason.AdminDisabled
            : human.Enabled == 0 ? PauseReason.Stopped
            : PauseReason.None;
        return new PairSide(side, humanId, true, pause);
    }
}
