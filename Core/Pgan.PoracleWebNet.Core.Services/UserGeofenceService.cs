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

        // Add kojiName to user's humans.area JSON array
        await this.AddAreaToHumanAsync(humanId, profileNo, kojiName);

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
        await this.RemoveAreaFromAllProfilesAsync(humanId, profileNo, geofence.KojiName);

        // Delete from local DB
        await this._repository.DeleteAsync(id);

        // Reload Poracle geofences
        await this.ReloadGeofencesSafeAsync();

        LogGeofenceDeleted(this._logger, geofence.KojiName, id, humanId);
    }

    public async Task<List<UserGeofence>> GetAllAsync() => await this._repository.GetAllAsync();

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
            // Look up the owning user's actual current profile (not hardcoded to 1)
            var owner = await this._humanRepository.GetByIdAsync(geofence.HumanId);
            var ownerProfileNo = owner?.CurrentProfileNo ?? 1;
            await this.RemoveAreaFromAllProfilesAsync(geofence.HumanId, ownerProfileNo, areaName);
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

        // If the name changed, update the user's humans.area
        if (promotedName != null && !string.Equals(promotedName, geofence.KojiName, StringComparison.Ordinal))
        {
            // Find the owning user's human record and swap area names
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

        await this.AddAreaToHumanAsync(humanId, profileNo, geofence.KojiName);
    }

    public async Task RemoveFromProfileAsync(string humanId, int profileNo, int geofenceId)
    {
        var geofence = await this._repository.GetByIdAsync(geofenceId)
            ?? throw new InvalidOperationException($"Geofence with ID {geofenceId} not found.");

        if (!string.Equals(geofence.HumanId, humanId, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Geofence does not belong to this user.");
        }

        await this.RemoveAreaFromHumanAsync(humanId, profileNo, geofence.KojiName);
    }

    public async Task<List<GeofenceRegion>> GetRegionsAsync() => await this._kojiService.GetRegionsAsync();

    private async Task AddAreaToHumanAsync(string humanId, int profileNo, string geofenceName)
    {
        var lowerName = geofenceName.ToLowerInvariant();

        // Update humans.area (Poracle's working copy for the active profile)
        var human = await this._humanRepository.GetByIdAndProfileAsync(humanId, profileNo);
        if (human != null)
        {
            var areas = ParseAreas(human.Area);
            if (!areas.Contains(lowerName))
            {
                areas.Add(lowerName);
                human.Area = JsonSerializer.Serialize(areas);
                await this._humanRepository.UpdateAsync(human);
            }
        }
        else
        {
            LogHumanNotFoundAddingArea(this._logger, humanId, profileNo);
        }

        // Also update profiles.area (PoracleJS syncs from this on profile switch)
        var profile = await this._profileRepository.GetByUserAndProfileNoAsync(humanId, profileNo);
        if (profile != null)
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

    private async Task RemoveAreaFromHumanAsync(string humanId, int profileNo, string geofenceName)
    {
        var lowerName = geofenceName.ToLowerInvariant();

        // Update humans.area (Poracle's working copy for the active profile)
        var human = await this._humanRepository.GetByIdAndProfileAsync(humanId, profileNo);
        if (human != null)
        {
            var areas = ParseAreas(human.Area);
            if (areas.Remove(lowerName))
            {
                human.Area = areas.Count > 0
                    ? JsonSerializer.Serialize(areas)
                    : "[]";
                await this._humanRepository.UpdateAsync(human);
            }
        }
        else
        {
            LogHumanNotFoundRemovingArea(this._logger, humanId, profileNo);
        }

        // Also update profiles.area (PoracleJS syncs from this on profile switch)
        var profile = await this._profileRepository.GetByUserAndProfileNoAsync(humanId, profileNo);
        if (profile != null)
        {
            var profileAreas = ParseAreas(profile.Area);
            if (profileAreas.Remove(lowerName))
            {
                profile.Area = profileAreas.Count > 0
                    ? JsonSerializer.Serialize(profileAreas)
                    : "[]";
                await this._profileRepository.UpdateAsync(profile);
            }
        }
    }

    /// <summary>
    /// Removes a geofence area name from humans.area (active profile) and all profiles.area entries.
    /// Sequential DB updates per profile are acceptable here — users typically have 2-4 profiles.
    /// </summary>
    private async Task RemoveAreaFromAllProfilesAsync(string humanId, int profileNo, string geofenceName)
    {
        var lowerName = geofenceName.ToLowerInvariant();

        // Remove from humans.area (active profile)
        await this.RemoveAreaFromHumanAsync(humanId, profileNo, geofenceName);

        // Remove from all profiles.area entries
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Human {HumanId} profile {ProfileNo} not found when adding area")]
    private static partial void LogHumanNotFoundAddingArea(ILogger logger, string humanId, int profileNo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Human {HumanId} profile {ProfileNo} not found when removing area")]
    private static partial void LogHumanNotFoundRemovingArea(ILogger logger, string humanId, int profileNo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to reload Poracle geofences after custom geofence change")]
    private static partial void LogGeofenceReloadFailed(ILogger logger, Exception ex);
}
