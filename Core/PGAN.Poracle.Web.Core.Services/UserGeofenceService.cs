using System.Text.Json;

using Microsoft.Extensions.Logging;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Services;

public class UserGeofenceService(
    IUserGeofenceRepository repository,
    IKojiService kojiService,
    IPoracleApiProxy poracleApiProxy,
    IHumanRepository humanRepository,
    IDiscordNotificationService discordNotificationService,
    ILogger<UserGeofenceService> logger) : IUserGeofenceService
{
    private const int MaxGeofencesPerUser = 10;

    private readonly IUserGeofenceRepository _repository = repository;
    private readonly IKojiService _kojiService = kojiService;
    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly IHumanRepository _humanRepository = humanRepository;
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
                    this._logger.LogWarning(ex, "Failed to deserialize polygon for geofence '{KojiName}'", g.KojiName);
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

        if (!System.Text.RegularExpressions.Regex.IsMatch(trimmedName, @"^[a-zA-Z0-9 \-'.()&]+$"))
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

        this._logger.LogInformation("Created custom geofence '{KojiName}' for user {HumanId}", kojiName, humanId);

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

        // Remove kojiName from user's humans.area JSON array
        await this.RemoveAreaFromHumanAsync(humanId, profileNo, geofence.KojiName);

        // Delete from local DB
        await this._repository.DeleteAsync(id);

        // Reload Poracle geofences
        await this.ReloadGeofencesSafeAsync();

        this._logger.LogInformation("Deleted custom geofence '{KojiName}' (ID {Id}) for user {HumanId}", geofence.KojiName, id, humanId);
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
                this._logger.LogWarning(ex, "Failed to remove approved geofence '{KojiName}' from Koji during admin delete", geofence.KojiName);
            }
        }

        // Remove from user's humans.area
        try
        {
            var areaName = geofence.Status == "approved" && geofence.PromotedName != null
                ? geofence.PromotedName.ToLowerInvariant()
                : geofence.KojiName;
            await this.RemoveAreaFromHumanAsync(geofence.HumanId, 1, areaName);
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Failed to remove area for geofence '{KojiName}' during admin delete", geofence.KojiName);
        }

        await this._repository.DeleteAsync(id);
        await this.ReloadGeofencesSafeAsync();

        this._logger.LogInformation("Admin {AdminId} deleted geofence '{KojiName}' (ID {Id}, status: {Status})",
            adminId, geofence.KojiName, id, geofence.Status);
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
                    this._logger.LogWarning(ex, "Failed to deserialize polygon for geofence '{KojiName}'", geofence.KojiName);
                }
            }

            try
            {
                mapImageUrl = await this._poracleApiProxy.GetAreaMapUrlAsync(geofence.KojiName);
            }
            catch (Exception ex)
            {
                this._logger.LogWarning(ex, "Failed to fetch static map for geofence '{KojiName}'", geofence.KojiName);
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
            this._logger.LogWarning(ex, "Failed to create Discord forum post for geofence submission '{KojiName}'", kojiName);
        }

        this._logger.LogInformation("User {HumanId} submitted geofence '{KojiName}' for review", humanId, kojiName);

        return updated;
    }

    public async Task<List<UserGeofence>> GetPendingSubmissionsAsync() => await this._repository.GetByStatusAsync("pending_review");

    public async Task<UserGeofence> ApproveSubmissionAsync(string adminId, int id, string? promotedName)
    {
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
                this._logger.LogWarning(ex, "Failed to post approval to Discord thread {ThreadId}", geofence.DiscordThreadId);
            }
        }

        this._logger.LogInformation("Admin {AdminId} approved geofence '{KojiName}' (ID {Id}), promotedName: {PromotedName}",
            adminId, geofence.KojiName, id, promotedName);

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
                this._logger.LogWarning(ex, "Failed to post rejection to Discord thread {ThreadId}", geofence.DiscordThreadId);
            }
        }

        this._logger.LogInformation("Admin {AdminId} rejected geofence '{KojiName}' (ID {Id})", adminId, geofence.KojiName, id);

        return updated;
    }

    public async Task<List<GeofenceRegion>> GetRegionsAsync() => await this._kojiService.GetRegionsAsync();

    private async Task AddAreaToHumanAsync(string humanId, int profileNo, string geofenceName)
    {
        var human = await this._humanRepository.GetByIdAndProfileAsync(humanId, profileNo);
        if (human == null)
        {
            this._logger.LogWarning("Human {HumanId} profile {ProfileNo} not found when adding area", humanId, profileNo);
            return;
        }

        var areas = ParseAreas(human.Area);
        var lowerName = geofenceName.ToLowerInvariant();
        if (!areas.Contains(lowerName))
        {
            areas.Add(lowerName);
        }

        human.Area = JsonSerializer.Serialize(areas);
        await this._humanRepository.UpdateAsync(human);
    }

    private async Task RemoveAreaFromHumanAsync(string humanId, int profileNo, string geofenceName)
    {
        var human = await this._humanRepository.GetByIdAndProfileAsync(humanId, profileNo);
        if (human == null)
        {
            this._logger.LogWarning("Human {HumanId} profile {ProfileNo} not found when removing area", humanId, profileNo);
            return;
        }

        var areas = ParseAreas(human.Area);
        areas.Remove(geofenceName.ToLowerInvariant());

        human.Area = areas.Count > 0
            ? JsonSerializer.Serialize(areas)
            : "[]";

        await this._humanRepository.UpdateAsync(human);
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
            await this._poracleApiProxy.ReloadGeofencesAsync();
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Failed to reload Poracle geofences after custom geofence change");
        }
    }

}
