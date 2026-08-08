using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

/// <inheritdoc cref="IUserPurgeService"/>
public partial class UserPurgeService(
    IHumanRepository humanRepository,
    IUserGeofenceRepository geofenceRepository,
    IUserGeofenceService geofenceService,
    IWebhookDelegateRepository webhookDelegateRepository,
    IQuickPickDefinitionRepository quickPickRepository,
    IQuickPickAppliedStateRepository appliedStateRepository,
    IHumanService humanService,
    ILogger<UserPurgeService> logger) : IUserPurgeService
{
    private readonly IHumanRepository _humanRepository = humanRepository;
    private readonly IUserGeofenceRepository _geofenceRepository = geofenceRepository;
    private readonly IUserGeofenceService _geofenceService = geofenceService;
    private readonly IWebhookDelegateRepository _webhookDelegateRepository = webhookDelegateRepository;
    private readonly IQuickPickDefinitionRepository _quickPickRepository = quickPickRepository;
    private readonly IQuickPickAppliedStateRepository _appliedStateRepository = appliedStateRepository;
    private readonly IHumanService _humanService = humanService;
    private readonly ILogger<UserPurgeService> _logger = logger;

    public async Task<bool> PurgeAsync(string userId)
    {
        if (!await this._humanRepository.ExistsAsync(userId))
        {
            return false;
        }

        // Order matters only at the end: the humans row goes last, so a failure part-way through leaves an
        // account that is still visible and still deletable rather than a half-erased ghost. Each step is
        // logged and swallowed for the same reason -- one unreachable dependency must not strand the rest.
        await this.TryAsync("alarms", () => this._humanService.DeleteAllAlarmsByUserAsync(userId));
        await this.TryAsync("geofences", () => this.PurgeGeofencesAsync(userId));
        await this.TryAsync("webhook delegates", () => this._webhookDelegateRepository.RemoveAllForIdAsync(userId));
        await this.TryAsync("quick picks", () => this.PurgeQuickPicksAsync(userId));

        return await this._humanRepository.DeleteUserAsync(userId);
    }

    /// <summary>
    /// Goes through the geofence service rather than the repository so a promoted fence is also removed from
    /// the shared Koji project, and so Poracle re-reads the feed. Otherwise a deleted user's polygons keep
    /// being served to PoracleJS. See #511.
    /// </summary>
    private async Task PurgeGeofencesAsync(string userId)
    {
        foreach (var geofence in await this._geofenceRepository.GetByHumanIdAsync(userId))
        {
            await this._geofenceService.AdminDeleteAsync(userId, geofence.Id);
        }
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
