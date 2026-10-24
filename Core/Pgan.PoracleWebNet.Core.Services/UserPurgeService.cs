using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

/// <inheritdoc cref="IUserPurgeService"/>
public partial class UserPurgeService(
    IUserGeofenceRepository geofenceRepository,
    IUserGeofenceService geofenceService,
    IWebhookDelegateRepository webhookDelegateRepository,
    IQuickPickDefinitionRepository quickPickRepository,
    IQuickPickAppliedStateRepository appliedStateRepository,
    IHumanService humanService,
    IAccountLinkRepository accountLinkRepository,
    ILinkedAccount linkedAccount,
    IUserAreaWriter areaWriter,
    ILogger<UserPurgeService> logger) : IUserPurgeService
{
    private readonly IUserGeofenceRepository _geofenceRepository = geofenceRepository;
    private readonly IUserGeofenceService _geofenceService = geofenceService;
    private readonly IWebhookDelegateRepository _webhookDelegateRepository = webhookDelegateRepository;
    private readonly IQuickPickDefinitionRepository _quickPickRepository = quickPickRepository;
    private readonly IQuickPickAppliedStateRepository _appliedStateRepository = appliedStateRepository;
    private readonly IHumanService _humanService = humanService;
    private readonly IAccountLinkRepository _accountLinkRepository = accountLinkRepository;
    private readonly ILinkedAccount _linkedAccount = linkedAccount;
    private readonly IUserAreaWriter _areaWriter = areaWriter;
    private readonly ILogger<UserPurgeService> _logger = logger;

    public async Task<bool> PurgeAsync(string userId)
    {
        // False only when PoracleNG answers 404 for this id. Anything else it fails to answer throws, and so
        // does this: the caller turns false into 404, and an outage must not tell an admin the account they
        // are deleting is already gone. This read went to Poracle's humans table directly until the proxy
        // could tell the two apart. See #994.
        if (!await this._humanService.ExistsAsync(userId))
        {
            return false;
        }

        // Order matters only at the end: the humans row goes last, so a failure part-way through leaves an
        // account that is still visible and still deletable rather than a half-erased ghost. Each step is
        // logged and swallowed for the same reason -- one unreachable dependency must not strand the rest.
        // The partner is read while the link still stands: once the account is gone the link is removed too.
        var partnerId = await this.PartnerOfAsync(userId);
        var heldByPartner = new List<string>();

        await this.TryAsync("alarms", () => this._humanService.DeleteAllAlarmsByUserAsync(userId));
        await this.TryAsync("geofences", () => this.PurgeGeofencesAsync(userId, heldByPartner));
        await this.TryAsync("geofence names on the linked account", () => this.CleanPartnerAreasAsync(partnerId, heldByPartner));
        await this.TryAsync("webhook delegates", () => this._webhookDelegateRepository.RemoveAllForIdAsync(userId));
        await this.TryAsync("quick picks", () => this.PurgeQuickPicksAsync(userId));

        var deleted = await this._humanService.DeleteUserAsync(userId);
        if (deleted)
        {
            // Only once the human is gone: a failed delete leaves the account, and so its link, in place.
            await this.TryAsync("account link", () => this.PurgeLinkAsync(userId));
            await this.TryAsync("leftover geofences", () => this._geofenceService.RemoveFencesOfDeletedAccountAsync(userId));
        }

        return deleted;
    }

    /// <summary>
    /// Goes through the geofence service rather than the repository so a promoted fence is also removed from
    /// the shared Koji project, and so Poracle re-reads the feed. Otherwise a deleted user's polygons keep
    /// being served to PoracleJS. See #511.
    /// </summary>
    /// <remarks>
    /// Alarms limited to a fence are deleted with it: the account is going, so there is nothing to keep them
    /// for (and the alarm step has normally removed them already). One fence that cannot be cleaned does not
    /// stop the rest. Its row and any Koji copy are kept, as any fence delete keeps them when PoracleNG fails,
    /// and <see cref="IUserGeofenceService.RemoveFencesOfDeletedAccountAsync"/> sweeps both once the account
    /// is gone: its area lists went with it, and a row left behind would keep serving the polygon in the
    /// geofence feed.
    /// </remarks>
    private async Task PurgeGeofencesAsync(string userId, List<string> heldByPartner)
    {
        foreach (var geofence in await this._geofenceRepository.GetByHumanIdAsync(userId))
        {
            // The delete cleans both humans of a linked pair and says so when the other one did not take it. A
            // delete that fails outright leaves the row to the leftovers sweep, which never touches an area list.
            // Either way the partner may still hold the name, and it is a live account.
            var cleaned = false;
            try
            {
                cleaned = await this._geofenceService.AdminDeleteAsync(userId, geofence.Id, deleteWidenedAlarms: true) is null;
            }
            catch (Exception ex)
            {
                LogPurgeStepFailed(this._logger, ex, $"geofence {geofence.Id}");
            }

            if (!cleaned)
            {
                heldByPartner.Add((string.IsNullOrEmpty(geofence.PromotedName) ? geofence.KojiName : geofence.PromotedName).ToLowerInvariant());
            }
        }
    }

    /// <summary>
    /// Removes the purged account's fence names from the linked partner's profiles and rules. One name that
    /// cannot be removed is logged and does not stop the next, nor the purge.
    /// </summary>
    private async Task CleanPartnerAreasAsync(string? partnerId, List<string> names)
    {
        if (partnerId is null)
        {
            return;
        }

        foreach (var name in names)
        {
            await this.TryAsync($"area {name} on the linked account", () => this._areaWriter.RemoveEverywhereAsync(partnerId, name, deleteWidenedAlarms: true));
        }
    }

    /// <summary>The other existing human of the account's pair, or null when it has none or it could not be read.</summary>
    private async Task<string?> PartnerOfAsync(string userId)
    {
        try
        {
            var pair = await this._linkedAccount.ResolveAsync(userId);
            return pair?.IsLinked == true
                ? pair.ExistingHumanIds.FirstOrDefault(id => !string.Equals(id, userId, StringComparison.Ordinal))
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPurgeStepFailed(this._logger, ex, "linked account lookup");
            return null;
        }
    }

    /// <summary>
    /// Removes the link holding this id. The link is read first because the partner's id is only known
    /// from it, and both ids are invalidated: <see cref="ILinkedAccount.Invalidate"/> finds a partner only
    /// through a cached link, which the removal is about to make stale.
    /// </summary>
    private async Task PurgeLinkAsync(string userId)
    {
        var link = await this._accountLinkRepository.GetByHumanIdAsync(userId);
        if (link is null)
        {
            return;
        }

        await this._accountLinkRepository.RemoveByHumanIdAsync(userId);
        this._linkedAccount.Invalidate(link.DiscordId);
        this._linkedAccount.Invalidate(link.TelegramId);
    }

    private async Task PurgeQuickPicksAsync(string userId)
    {
        // Applied state first: it is keyed on the definition, and a global pick the user applied leaves a row
        // that no definition delete would reach.
        await this._appliedStateRepository.DeleteByUserAsync(userId);

        foreach (var definition in await this._quickPickRepository.GetByOwnerAsync(userId))
        {
            await this._quickPickRepository.DeleteByIdAndOwnerAsync(definition.Id, userId);
        }
    }

    private async Task TryAsync(string step, Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            LogPurgeStepFailed(this._logger, ex, step);
        }
    }

    // Its own try/catch rather than delegating to the overload above: an async lambda wrapping a Func<Task<T>>
    // binds back to this method, which recurses until the stack goes.
    private async Task TryAsync<T>(string step, Func<Task<T>> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            LogPurgeStepFailed(this._logger, ex, step);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Could not remove {Step} while deleting a user; the account was still deleted and the leftovers need clearing by hand.")]
    private static partial void LogPurgeStepFailed(ILogger logger, Exception exception, string step);
}
