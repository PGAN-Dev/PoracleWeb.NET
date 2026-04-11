using System.Text.Json;

using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public partial class UserGeofenceService(
    IUserGeofenceRepository repository,
    IKojiService kojiService,
    IPoracleApiProxy poracleApiProxy,
    IPoracleServerService poracleServerService,
    IPoracleHumanProxy humanProxy,
    IHumanRepository humanRepository,
    IProfileRepository profileRepository,
    IDiscordNotificationService discordNotificationService,
    ILogger<UserGeofenceService> logger) : IUserGeofenceService
{
    private const int MaxGeofencesPerUser = 10;

    private readonly IUserGeofenceRepository _repository = repository;
    private readonly IKojiService _kojiService = kojiService;
    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly IPoracleServerService _poracleServerService = poracleServerService;
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IHumanRepository _humanRepository = humanRepository;
    private readonly IProfileRepository _profileRepository = profileRepository;
    private readonly IDiscordNotificationService _discordNotificationService = discordNotificationService;
    private readonly ILogger<UserGeofenceService> _logger = logger;

    public async Task<List<UserGeofence>> GetByUserAsync(string humanId)
    {
        var geofences = await this._repository.GetByHumanIdAsync(humanId);

        foreach (var g in geofences)
        {
            if (!string.IsNullOrEmpty(g.PolygonJson))
            {
                try
                {
                    g.Polygon = JsonSerializer.Deserialize<double[][]>(g.PolygonJson);
                }
                catch (JsonException ex)
                {
                    LogPolygonDeserializationFailed(this._logger, ex, g.KojiName);
                }
            }
        }

        return geofences;
    }

    public async Task<UserGeofence> CreateAsync(string humanId, int profileNo, UserGeofenceCreate model)
    {
        // Check count limit via local DB
        var count = await this._repository.GetCountByHumanIdAsync(humanId);
        if (count >= MaxGeofencesPerUser)
        {
            throw new InvalidOperationException($"Maximum of {MaxGeofencesPerUser} custom geofences per user reached.");
        }

        // Validate display name (server-side, matching frontend regex)
        var trimmedName = model.DisplayName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmedName) || trimmedName.Length > 50)
        {
            throw new InvalidOperationException("Display name must be between 1 and 50 characters.");
        }

        if (!MyRegex().IsMatch(trimmedName))
        {
            throw new InvalidOperationException("Display name contains invalid characters.");
        }

        // Validate polygon point count
        if (model.Polygon.Length > 500)
        {
            throw new InvalidOperationException("Polygon cannot exceed 500 points.");
        }

        if (model.Polygon.Length < 3)
        {
            throw new InvalidOperationException("Polygon must have at least 3 points.");
        }

        // Use lowercase display name as the Koji geofence name
        // Must be lowercase because Poracle does case-sensitive area matching
        // and humans.area stores names in lowercase
        var kojiName = model.DisplayName.Trim().ToLowerInvariant();

        // Check for collision with existing geofences (our DB + Koji)
        var existing = await this._repository.GetByKojiNameAsync(kojiName);
        if (existing != null)
        {
            var baseName = kojiName;
            var found = false;
            for (var i = 2; i <= 10; i++)
            {
                kojiName = $"{baseName} {i}";
                existing = await this._repository.GetByKojiNameAsync(kojiName);
                if (existing == null)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                throw new InvalidOperationException($"Unable to generate a unique geofence name for '{model.DisplayName}'. Please choose a different name.");
            }
        }

        // Serialize polygon to JSON and store locally (not in Koji)
        var polygonJson = JsonSerializer.Serialize(model.Polygon);

        // Create local DB record with polygon data
        var geofence = await this._repository.CreateAsync(new UserGeofence
        {
            HumanId = humanId,
            KojiName = kojiName,
            DisplayName = model.DisplayName,
            GroupName = model.GroupName,
            ParentId = model.ParentId,
            PolygonJson = polygonJson,
            Status = "active",
        });

        // Add kojiName to user's area list via proxy (handles humans.area + profiles.area dual-write)
        await this.AddAreaToHumanAsync(humanId, kojiName);

        // Reload Poracle geofences (Poracle reads from our feed + Koji)
        await this.ReloadGeofencesSafeAsync();

        // Set polygon on result from input
        geofence.Polygon = model.Polygon;

        LogGeofenceCreated(this._logger, kojiName, humanId);

        return geofence;
    }

    public async Task DeleteAsync(string humanId, int profileNo, int id)
    {
        var geofence = await this._repository.GetByIdAsync(id)
            ?? throw new InvalidOperationException($"Geofence with ID {id} not found.");

        if (!string.Equals(geofence.HumanId, humanId, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Geofence does not belong to this user.");
        }

        // Remove kojiName from all profiles (humans.area + profiles.area)
        await this.RemoveAreaFromAllProfilesAsync(humanId, geofence.KojiName);

        // Delete from local DB
        await this._repository.DeleteAsync(id);

        // Reload Poracle geofences
        await this.ReloadGeofencesSafeAsync();

        LogGeofenceDeleted(this._logger, geofence.KojiName, id, humanId);
    }

    public async Task<List<UserGeofence>> GetAllAsync() => await this._repository.GetAllAsync();

    public async Task<List<UserGeofence>> GetAllWithDetailsAsync()
    {
        var geofences = await this.GetAllAsync();

        // Batch-fetch owner humans by distinct HumanId
        var humanIds = geofences.Select(g => g.HumanId).Distinct().ToList();

        // Also fetch reviewer humans (reviewedBy is a humanId)
        var reviewerIds = geofences
            .Where(g => !string.IsNullOrEmpty(g.ReviewedBy))
            .Select(g => g.ReviewedBy!)
            .Distinct()
            .ToList();

        // Merge all IDs for a single batch lookup
        var allIds = humanIds.Union(reviewerIds).Distinct().ToList();
        var humans = await this._humanRepository.GetByIdsAsync(allIds);
        var humanLookup = humans.ToDictionary(h => h.Id, h => h);

        foreach (var g in geofences)
        {
            // Enrich owner name
            g.OwnerName = humanLookup.TryGetValue(g.HumanId, out var human)
                ? human.Name ?? g.HumanId
                : g.HumanId;

            // Enrich reviewer name
            if (!string.IsNullOrEmpty(g.ReviewedBy))
            {
                g.ReviewedByName = humanLookup.TryGetValue(g.ReviewedBy, out var reviewer)
                    ? reviewer.Name ?? g.ReviewedBy
                    : g.ReviewedBy;
            }

            // Parse polygon and set point count
            if (!string.IsNullOrEmpty(g.PolygonJson))
            {
                try
                {
                    g.Polygon = JsonSerializer.Deserialize<double[][]>(g.PolygonJson);
                    g.PointCount = g.Polygon?.Length ?? 0;
                }
                catch (JsonException ex)
                {
                    LogPolygonDeserializationFailed(this._logger, ex, g.KojiName);
                }
            }
        }

        return geofences;
    }

    public async Task AdminDeleteAsync(string adminId, int id)
    {
        var geofence = await this._repository.GetByIdAsync(id)
            ?? throw new InvalidOperationException($"Geofence with ID {id} not found.");

        // If approved (promoted to Koji), remove from Koji too
        if (geofence.Status == "approved")
        {
            try
            {
                var name = geofence.PromotedName ?? geofence.KojiName;
                await this._kojiService.RemoveGeofenceFromProjectAsync(name);
            }
            catch (Exception ex)
            {
                LogKojiRemovalFailed(this._logger, ex, geofence.KojiName);
            }
        }

        // Remove from user's area across all profiles
        try
        {
            var areaName = geofence.Status == "approved" && geofence.PromotedName != null
                ? geofence.PromotedName.ToLowerInvariant()
                : geofence.KojiName;
            await this.RemoveAreaFromAllProfilesAsync(geofence.HumanId, areaName);
        }
        catch (Exception ex)
        {
            LogAreaRemovalFailed(this._logger, ex, geofence.KojiName);
        }

        await this._repository.DeleteAsync(id);
        await this.ReloadGeofencesSafeAsync();

        LogAdminDeletedGeofence(this._logger, adminId, geofence.KojiName, id, geofence.Status);
    }

    public async Task<UserGeofence> SubmitForReviewAsync(string humanId, string kojiName)
    {
        var geofence = await this._repository.GetByKojiNameAsync(kojiName)
            ?? throw new InvalidOperationException($"Geofence '{kojiName}' not found.");

        if (!string.Equals(geofence.HumanId, humanId, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Geofence does not belong to this user.");
        }

        if (geofence.Status != "active")
        {
            throw new InvalidOperationException($"Geofence must be in 'active' status to submit for review. Current status: '{geofence.Status}'.");
        }

        geofence.Status = "pending_review";
        geofence.SubmittedAt = DateTime.UtcNow;

        var updated = await this._repository.UpdateAsync(geofence);

        // Create Discord forum post for the submission
        try
        {
            var human = await this._humanRepository.GetByIdAndProfileAsync(humanId, 1);
            var userName = human?.Name ?? humanId;

            // Get polygon point count and static map from Poracle
            var polygonPoints = 0;
            string? mapImageUrl = null;
            if (!string.IsNullOrEmpty(geofence.PolygonJson))
            {
                try
                {
                    var polygon = JsonSerializer.Deserialize<double[][]>(geofence.PolygonJson);
                    polygonPoints = polygon?.Length ?? 0;
                }
                catch (JsonException ex)
                {
                    LogPolygonDeserializationFailed(this._logger, ex, geofence.KojiName);
                }
            }

            try
            {
                mapImageUrl = await this._poracleApiProxy.GetAreaMapUrlAsync(geofence.KojiName);
            }
            catch (Exception ex)
            {
                LogStaticMapFetchFailed(this._logger, ex, geofence.KojiName);
            }

            var threadId = await this._discordNotificationService.CreateGeofenceSubmissionPostAsync(
                humanId, userName, geofence.DisplayName, geofence.GroupName, polygonPoints, mapImageUrl);

            if (threadId != null)
            {
                updated.DiscordThreadId = threadId;
                updated = await this._repository.UpdateAsync(updated);
            }
        }
        catch (Exception ex)
        {
            LogDiscordForumPostCreationFailed(this._logger, ex, kojiName);
        }

        LogGeofenceSubmittedForReview(this._logger, humanId, kojiName);

        return updated;
    }

    public async Task<List<UserGeofence>> GetPendingSubmissionsAsync() => await this._repository.GetByStatusAsync("pending_review");

    public async Task<UserGeofence> ApproveSubmissionAsync(string adminId, int id, string? promotedName)
    {
        // Validate promotedName with the same rules as display names
        if (promotedName != null)
        {
            var trimmedPromoted = promotedName.Trim();
            if (string.IsNullOrEmpty(trimmedPromoted) || trimmedPromoted.Length > 50)
            {
                throw new InvalidOperationException("Promoted name must be between 1 and 50 characters.");
            }

            if (!MyRegex().IsMatch(trimmedPromoted))
            {
                throw new InvalidOperationException("Promoted name contains invalid characters.");
            }

            promotedName = trimmedPromoted;
        }

        var geofence = await this._repository.GetByIdAsync(id)
            ?? throw new InvalidOperationException($"Geofence with ID {id} not found.");

        // Parse polygon from local DB
        if (string.IsNullOrEmpty(geofence.PolygonJson))
        {
            throw new InvalidOperationException($"Geofence '{geofence.KojiName}' has no polygon data stored locally.");
        }

        var polygon = JsonSerializer.Deserialize<double[][]>(geofence.PolygonJson)
            ?? throw new InvalidOperationException($"Failed to deserialize polygon for geofence '{geofence.KojiName}'.");

        // Save to Koji as a public geofence (userSelectable + displayInMatches = true)
        var targetName = promotedName ?? geofence.KojiName;
        await this._kojiService.SaveGeofenceAsync(
            targetName, geofence.DisplayName, geofence.GroupName, geofence.ParentId, polygon, isPublic: true);

        // If the name changed, update the user's area list via proxy
        if (promotedName != null && !string.Equals(promotedName, geofence.KojiName, StringComparison.Ordinal))
        {
            try
            {
                var currentAreas = await this.GetCurrentAreasAsync(geofence.HumanId);
                var oldLower = geofence.KojiName.ToLowerInvariant();
                var newLower = promotedName.ToLowerInvariant();
                if (currentAreas.Remove(oldLower))
                {
                    currentAreas.Add(newLower);
                    await this._humanProxy.SetAreasAsync(geofence.HumanId, [.. currentAreas]);
                }
            }
            catch (Exception ex)
            {
                LogProxyAreaSwapFailed(this._logger, ex, geofence.KojiName, promotedName);
                // Fallback to direct DB for the area swap
                try
                {
                    var human = await this._humanRepository.GetByIdAndProfileAsync(geofence.HumanId, 1);
                    if (human != null)
                    {
                        var areas = ParseAreas(human.Area);
                        var oldLower = geofence.KojiName.ToLowerInvariant();
                        var newLower = promotedName.ToLowerInvariant();
                        if (areas.Remove(oldLower))
                        {
                            areas.Add(newLower);
                            human.Area = JsonSerializer.Serialize(areas);
                            await this._humanRepository.UpdateAsync(human);
                        }
                    }
                }
                catch (Exception innerEx)
                {
                    LogAreaSwapFallbackFailed(this._logger, innerEx, geofence.KojiName);
                }
            }
        }

        // Update local record
        geofence.Status = "approved";
        geofence.ReviewedBy = adminId;
        geofence.ReviewedAt = DateTime.UtcNow;
        geofence.PromotedName = promotedName;

        var updated = await this._repository.UpdateAsync(geofence);

        // Update group_map.json on all Poracle servers so the promoted geofence shows with correct group
        try
        {
            await this._poracleServerService.UpdateGroupMapAsync(targetName, geofence.GroupName);
        }
        catch (Exception ex)
        {
            LogGroupMapUpdateFailed(this._logger, ex, targetName);
        }

        // Reload Poracle geofences
        await this.ReloadGeofencesSafeAsync();

        // Notify Discord forum thread
        if (!string.IsNullOrEmpty(geofence.DiscordThreadId))
        {
            try
            {
                await this._discordNotificationService.PostApprovalMessageAsync(
                    geofence.DiscordThreadId, geofence.DisplayName, promotedName ?? geofence.DisplayName);
            }
            catch (Exception ex)
            {
                LogApprovalDiscordPostFailed(this._logger, ex, geofence.DiscordThreadId);
            }
        }

        LogGeofenceApproved(this._logger, adminId, geofence.KojiName, id, promotedName);

        return updated;
    }

    public async Task<UserGeofence> RejectSubmissionAsync(string adminId, int id, string reviewNotes)
    {
        var geofence = await this._repository.GetByIdAsync(id)
            ?? throw new InvalidOperationException($"Geofence with ID {id} not found.");

        geofence.Status = "rejected";
        geofence.ReviewedBy = adminId;
        geofence.ReviewedAt = DateTime.UtcNow;
        geofence.ReviewNotes = reviewNotes;

        var updated = await this._repository.UpdateAsync(geofence);

        // Notify Discord forum thread
        if (!string.IsNullOrEmpty(geofence.DiscordThreadId))
        {
            try
            {
                await this._discordNotificationService.PostRejectionMessageAsync(
                    geofence.DiscordThreadId, geofence.DisplayName, reviewNotes);
            }
            catch (Exception ex)
            {
                LogRejectionDiscordPostFailed(this._logger, ex, geofence.DiscordThreadId);
            }
        }

        LogGeofenceRejected(this._logger, adminId, geofence.KojiName, id);

        return updated;
    }

    public async Task AddToProfileAsync(string humanId, int profileNo, int geofenceId)
    {
        var geofence = await this._repository.GetByIdAsync(geofenceId)
            ?? throw new InvalidOperationException($"Geofence with ID {geofenceId} not found.");

        if (!string.Equals(geofence.HumanId, humanId, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Geofence does not belong to this user.");
        }

        await this.AddAreaToHumanAsync(humanId, geofence.KojiName);

        // The proxy-based setAreas path used to trigger PoracleNG's internal reloadState
        // automatically. Since we now write directly to the DB, we must ask PoracleNG to
        // reload its in-memory state so the toggle takes effect on the next alarm event.
        await this.ReloadGeofencesSafeAsync();
    }

    public async Task RemoveFromProfileAsync(string humanId, int profileNo, int geofenceId)
    {
        var geofence = await this._repository.GetByIdAsync(geofenceId)
            ?? throw new InvalidOperationException($"Geofence with ID {geofenceId} not found.");

        if (!string.Equals(geofence.HumanId, humanId, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Geofence does not belong to this user.");
        }

        await this.RemoveAreaFromHumanAsync(humanId, geofence.KojiName);
        await this.ReloadGeofencesSafeAsync();
    }

    public async Task<List<GeofenceRegion>> GetRegionsAsync() => await this._kojiService.GetRegionsAsync();

    // HACK: trusted-set-areas (see docs/poracleng-enhancement-requests.md)
    // Re-adds user-owned geofence names via direct DB after PoracleNG's setAreas stripped them.
    // Remove this method and its callsite in AreaController.UpdateAreas once PoracleNG ships a
    // trusted setAreas variant (query flag or dedicated endpoint) that skips the userSelectable
    // intersection filter. Trust is already established via X-Poracle-Secret.
    public async Task<IReadOnlyList<string>> PreserveOwnedAreasInHumanAsync(string humanId, IReadOnlyCollection<string> candidateAreaNames)
    {
        if (candidateAreaNames.Count == 0)
        {
            return [];
        }

        // Only write back names that the user actually owns as custom geofences.
        var owned = await this._repository.GetByHumanIdAsync(humanId);
        if (owned.Count == 0)
        {
            return [];
        }

        var ownedNames = new HashSet<string>(owned.Select(g => g.KojiName.ToLowerInvariant()));
        var toRestore = candidateAreaNames
            .Select(a => a.ToLowerInvariant())
            .Where(ownedNames.Contains)
            .Distinct()
            .ToList();

        foreach (var name in toRestore)
        {
            await this.AddAreaToHumanAsync(humanId, name);
        }

        // Direct-DB writes bypass PoracleNG's internal reloadState — ask it to refresh
        // so the preserved geofences take effect immediately.
        if (toRestore.Count > 0)
        {
            await this.ReloadGeofencesSafeAsync();
        }

        return toRestore;
    }

    /// <summary>
    /// Adds a user-drawn geofence area name to the user's active profile area list by writing
    /// directly to <c>humans.area</c> and the current <c>profiles.area</c> row in the Poracle DB.
    /// Bypasses PoracleNG's <c>/setAreas</c> endpoint, which silently drops user geofences because
    /// they are served from the PoracleWeb feed with <c>userSelectable=false</c>.
    /// </summary>
    // HACK: trusted-set-areas (see docs/poracleng-enhancement-requests.md)
    // Direct-DB write scoped to user geofence names only. Revert to IPoracleHumanProxy.SetAreasAsync
    // once PoracleNG ships a trusted setAreas variant that skips the userSelectable intersection.
    // Regression history: this is the restored pre-#88 behavior; the proxy migration in v2.0.0
    // introduced the silent-strip bug because PoracleNG's filter discards userSelectable=false.
    private async Task AddAreaToHumanAsync(string humanId, string geofenceName)
    {
        var lowerName = geofenceName.ToLowerInvariant();

        var human = await this._humanRepository.GetByIdAsync(humanId);
        if (human is null)
        {
            return;
        }

        var humanAreas = ParseAreas(human.Area);
        if (!humanAreas.Contains(lowerName))
        {
            humanAreas.Add(lowerName);
            human.Area = JsonSerializer.Serialize(humanAreas);
            await this._humanRepository.UpdateAsync(human);
        }

        // Dual-write to the currently active profile so the state survives profile switches.
        var profile = await this._profileRepository.GetByUserAndProfileNoAsync(humanId, human.CurrentProfileNo);
        if (profile is not null)
        {
            var profileAreas = ParseAreas(profile.Area);
            if (!profileAreas.Contains(lowerName))
            {
                profileAreas.Add(lowerName);
                profile.Area = JsonSerializer.Serialize(profileAreas);
                await this._profileRepository.UpdateAsync(profile);
            }
        }
    }

    /// <summary>
    /// Removes a user-drawn geofence area name from the user's active profile area list by writing
    /// directly to <c>humans.area</c> and the current <c>profiles.area</c> row in the Poracle DB.
    /// Mirror of <see cref="AddAreaToHumanAsync"/>.
    /// </summary>
    // HACK: trusted-set-areas (see docs/poracleng-enhancement-requests.md)
    // Same rationale as AddAreaToHumanAsync — the proxy path strips user geofences on every write.
    private async Task RemoveAreaFromHumanAsync(string humanId, string geofenceName)
    {
        var lowerName = geofenceName.ToLowerInvariant();

        var human = await this._humanRepository.GetByIdAsync(humanId);
        if (human is null)
        {
            return;
        }

        var humanAreas = ParseAreas(human.Area);
        if (humanAreas.Remove(lowerName))
        {
            human.Area = humanAreas.Count > 0 ? JsonSerializer.Serialize(humanAreas) : "[]";
            await this._humanRepository.UpdateAsync(human);
        }

        var profile = await this._profileRepository.GetByUserAndProfileNoAsync(humanId, human.CurrentProfileNo);
        if (profile is not null)
        {
            var profileAreas = ParseAreas(profile.Area);
            if (profileAreas.Remove(lowerName))
            {
                profile.Area = profileAreas.Count > 0 ? JsonSerializer.Serialize(profileAreas) : "[]";
                await this._profileRepository.UpdateAsync(profile);
            }
        }
    }

    /// <summary>
    /// Removes a geofence area name from <c>humans.area</c> and every profile in <c>profiles.area</c>
    /// for the user. Used on geofence delete so the stale name is wiped out everywhere.
    /// </summary>
    // HACK: trusted-set-areas (see docs/poracleng-enhancement-requests.md)
    // Direct-DB loop across all profiles — the proxy path can only touch the active profile
    // and would strip user geofences on every write. Revert both once PoracleNG ships a trusted
    // setAreas variant (or a per-profile setAreas endpoint — see Atomic Area Update).
    private async Task RemoveAreaFromAllProfilesAsync(string humanId, string geofenceName)
    {
        var lowerName = geofenceName.ToLowerInvariant();

        var human = await this._humanRepository.GetByIdAsync(humanId);
        if (human is not null)
        {
            var humanAreas = ParseAreas(human.Area);
            if (humanAreas.Remove(lowerName))
            {
                human.Area = humanAreas.Count > 0 ? JsonSerializer.Serialize(humanAreas) : "[]";
                await this._humanRepository.UpdateAsync(human);
            }
        }

        var profiles = await this._profileRepository.GetByUserAsync(humanId);
        foreach (var profile in profiles)
        {
            var areas = ParseAreas(profile.Area);
            if (areas.Remove(lowerName))
            {
                profile.Area = areas.Count > 0
                    ? JsonSerializer.Serialize(areas)
                    : "[]";
                await this._profileRepository.UpdateAsync(profile);
            }
        }
    }

    /// <summary>
    /// Gets the current area list from <c>humans.area</c> via the PoracleNG proxy. Used by
    /// <see cref="ApproveSubmissionAsync"/> for name-swap bookkeeping.
    /// </summary>
    private async Task<List<string>> GetCurrentAreasAsync(string humanId)
    {
        var humanJson = await this._humanProxy.GetHumanAsync(humanId);
        if (humanJson is not null)
        {
            var areaStr = humanJson.Value.GetStringPropOrNull("area");
            return ParseAreas(areaStr);
        }

        return [];
    }

    private static List<string> ParseAreas(string? areaJson)
    {
        if (string.IsNullOrWhiteSpace(areaJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(areaJson) ?? [];
        }
        catch
        {
            // Fallback: treat as comma-separated
            return [.. areaJson.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        }
    }

    private async Task ReloadGeofencesSafeAsync()
    {
        try
        {
            this._kojiService.InvalidateAdminGeofenceCache();
            await this._poracleApiProxy.ReloadGeofencesAsync();
        }
        catch (Exception ex)
        {
            LogGeofenceReloadFailed(this._logger, ex);
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^[a-zA-Z0-9 \-'.()&]+$")]
    private static partial System.Text.RegularExpressions.Regex MyRegex();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to deserialize polygon for geofence '{KojiName}'")]
    private static partial void LogPolygonDeserializationFailed(ILogger logger, Exception ex, string kojiName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created custom geofence '{KojiName}' for user {HumanId}")]
    private static partial void LogGeofenceCreated(ILogger logger, string kojiName, string humanId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted custom geofence '{KojiName}' (ID {Id}) for user {HumanId}")]
    private static partial void LogGeofenceDeleted(ILogger logger, string kojiName, int id, string humanId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to remove approved geofence '{KojiName}' from Koji during admin delete")]
    private static partial void LogKojiRemovalFailed(ILogger logger, Exception ex, string kojiName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to remove area for geofence '{KojiName}' during admin delete")]
    private static partial void LogAreaRemovalFailed(ILogger logger, Exception ex, string kojiName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {AdminId} deleted geofence '{KojiName}' (ID {Id}, status: {Status})")]
    private static partial void LogAdminDeletedGeofence(ILogger logger, string adminId, string kojiName, int id, string status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to fetch static map for geofence '{KojiName}'")]
    private static partial void LogStaticMapFetchFailed(ILogger logger, Exception ex, string kojiName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to create Discord forum post for geofence submission '{KojiName}'")]
    private static partial void LogDiscordForumPostCreationFailed(ILogger logger, Exception ex, string kojiName);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {HumanId} submitted geofence '{KojiName}' for review")]
    private static partial void LogGeofenceSubmittedForReview(ILogger logger, string humanId, string kojiName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to update group_map.json for geofence '{Name}'")]
    private static partial void LogGroupMapUpdateFailed(ILogger logger, Exception ex, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to post approval to Discord thread {ThreadId}")]
    private static partial void LogApprovalDiscordPostFailed(ILogger logger, Exception ex, string threadId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {AdminId} approved geofence '{KojiName}' (ID {Id}), promotedName: {PromotedName}")]
    private static partial void LogGeofenceApproved(ILogger logger, string adminId, string kojiName, int id, string? promotedName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to post rejection to Discord thread {ThreadId}")]
    private static partial void LogRejectionDiscordPostFailed(ILogger logger, Exception ex, string threadId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Admin {AdminId} rejected geofence '{KojiName}' (ID {Id})")]
    private static partial void LogGeofenceRejected(ILogger logger, string adminId, string kojiName, int id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to reload Poracle geofences after custom geofence change")]
    private static partial void LogGeofenceReloadFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Proxy area swap failed for geofence '{KojiName}' → '{PromotedName}', trying direct DB fallback")]
    private static partial void LogProxyAreaSwapFailed(ILogger logger, Exception ex, string kojiName, string promotedName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Direct DB fallback also failed for area swap on geofence '{KojiName}'")]
    private static partial void LogAreaSwapFallbackFailed(ILogger logger, Exception ex, string kojiName);
}
