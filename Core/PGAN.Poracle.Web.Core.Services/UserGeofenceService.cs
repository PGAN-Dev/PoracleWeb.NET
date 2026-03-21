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

        var tasks = geofences.Select(async g =>
        {
            try
            {
                g.Polygon = await this._kojiService.GetGeofencePolygonAsync(g.KojiName);
            }
            catch (Exception ex)
            {
                this._logger.LogWarning(ex, "Failed to fetch polygon for geofence '{KojiName}'", g.KojiName);
            }
        });
        await Task.WhenAll(tasks);

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
            for (int i = 2; i <= 10; i++)
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

        // Save to Koji
        await this._kojiService.SaveGeofenceAsync(kojiName, model.DisplayName, model.GroupName, model.ParentId, model.Polygon);

        // Create local DB record
        var geofence = await this._repository.CreateAsync(new UserGeofence
        {
            HumanId = humanId,
            KojiName = kojiName,
            DisplayName = model.DisplayName,
            GroupName = model.GroupName,
            ParentId = model.ParentId,
            Status = "active",
        });

        // Add kojiName to user's humans.area JSON array
        await this.AddAreaToHumanAsync(humanId, profileNo, kojiName);

        // Reload Poracle geofences
        await this.ReloadGeofencesSafeAsync();

        // Fetch polygon from Koji and set on result
        try
        {
            geofence.Polygon = await this._kojiService.GetGeofencePolygonAsync(kojiName);
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Failed to fetch polygon for newly created geofence '{KojiName}'", kojiName);
            geofence.Polygon = model.Polygon;
        }

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

        // Remove from Koji project
        await this._kojiService.RemoveGeofenceFromProjectAsync(geofence.KojiName);

        // Remove kojiName from user's humans.area JSON array
        await this.RemoveAreaFromHumanAsync(humanId, profileNo, geofence.KojiName);

        // Delete from local DB
        await this._repository.DeleteAsync(id);

        // Reload Poracle geofences
        await this.ReloadGeofencesSafeAsync();

        this._logger.LogInformation("Deleted custom geofence '{KojiName}' (ID {Id}) for user {HumanId}", geofence.KojiName, id, humanId);
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
            try
            {
                var polygon = await this._kojiService.GetGeofencePolygonAsync(geofence.KojiName);
                polygonPoints = polygon?.Length ?? 0;
            }
            catch (Exception ex)
            {
                this._logger.LogWarning(ex, "Failed to fetch polygon point count for geofence '{KojiName}'", geofence.KojiName);
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

    public async Task<List<UserGeofence>> GetPendingSubmissionsAsync()
    {
        return await this._repository.GetByStatusAsync("pending_review");
    }

    public async Task<UserGeofence> ApproveSubmissionAsync(string adminId, int id, string? promotedName)
    {
        var geofence = await this._repository.GetByIdAsync(id)
            ?? throw new InvalidOperationException($"Geofence with ID {id} not found.");

        // Promote in Koji
        await this._kojiService.PromoteGeofenceAsync(geofence.KojiName, promotedName, geofence.DisplayName, geofence.GroupName, geofence.ParentId);

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

    public async Task<List<GeofenceRegion>> GetRegionsAsync()
    {
        return await this._kojiService.GetRegionsAsync();
    }

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
