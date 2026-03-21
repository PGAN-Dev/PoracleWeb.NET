using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PGAN.Poracle.Web.Core.Abstractions.Repositories;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Services;

public partial class UserGeofenceService(
    IUserGeofenceRepository userGeofenceRepository,
    IKojiService kojiService,
    IPoracleApiProxy poracleApiProxy,
    IHumanRepository humanRepository,
    ILogger<UserGeofenceService> logger) : IUserGeofenceService
{
    private const int MaxGeofencesPerUser = 10;
    private const int MaxSlugLength = 30;

    private readonly IUserGeofenceRepository _userGeofenceRepository = userGeofenceRepository;
    private readonly IKojiService _kojiService = kojiService;
    private readonly IPoracleApiProxy _poracleApiProxy = poracleApiProxy;
    private readonly IHumanRepository _humanRepository = humanRepository;
    private readonly ILogger<UserGeofenceService> _logger = logger;

    public async Task<List<UserGeofence>> GetByUserAsync(string humanId, int profileNo)
    {
        return await this._userGeofenceRepository.GetByHumanIdAsync(humanId, profileNo);
    }

    public async Task<UserGeofence> CreateAsync(string humanId, int profileNo, UserGeofenceCreate model)
    {
        // Check count limit
        var count = await this._userGeofenceRepository.GetCountByHumanIdAsync(humanId);
        if (count >= MaxGeofencesPerUser)
        {
            throw new InvalidOperationException($"Maximum of {MaxGeofencesPerUser} custom geofences per user reached.");
        }

        // Generate namespaced name
        var slug = GenerateSlug(model.DisplayName);
        var geofenceName = $"pweb_{humanId}_{slug}";

        // Serialize polygon to GeoJSON for local storage
        var polygonJson = JsonSerializer.Serialize(model.Polygon);

        // Save to Koji
        await this._kojiService.SaveGeofenceAsync(geofenceName, model.DisplayName, model.GroupName, model.ParentId, model.Polygon);

        // Save local tracking record
        var geofence = new UserGeofence
        {
            HumanId = humanId,
            ProfileNo = profileNo,
            GeofenceName = geofenceName,
            DisplayName = model.DisplayName,
            GroupName = model.GroupName,
            ParentId = model.ParentId,
            PolygonJson = polygonJson,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var created = await this._userGeofenceRepository.CreateAsync(geofence);

        // Add geofence name to user's humans.area JSON array
        await this.AddAreaToHumanAsync(humanId, profileNo, geofenceName);

        // Reload Poracle geofences
        await this.ReloadGeofencesSafeAsync();

        this._logger.LogInformation("Created custom geofence '{GeofenceName}' for user {HumanId}", geofenceName, humanId);

        return created;
    }

    public async Task<UserGeofence> UpdateAsync(string humanId, int id, UserGeofenceCreate model)
    {
        var existing = await this._userGeofenceRepository.GetByIdAsync(id)
            ?? throw new InvalidOperationException($"Geofence with id {id} not found.");

        if (existing.HumanId != humanId)
        {
            throw new UnauthorizedAccessException("Geofence does not belong to this user.");
        }

        // Update polygon in Koji (same name, updated geometry)
        var polygonJson = JsonSerializer.Serialize(model.Polygon);
        await this._kojiService.SaveGeofenceAsync(existing.GeofenceName, model.DisplayName, model.GroupName, model.ParentId, model.Polygon);

        // Update local record
        existing.DisplayName = model.DisplayName;
        existing.GroupName = model.GroupName;
        existing.ParentId = model.ParentId;
        existing.PolygonJson = polygonJson;

        var updated = await this._userGeofenceRepository.UpdateAsync(existing);

        // Reload Poracle geofences
        await this.ReloadGeofencesSafeAsync();

        this._logger.LogInformation("Updated custom geofence '{GeofenceName}' for user {HumanId}", existing.GeofenceName, humanId);

        return updated;
    }

    public async Task DeleteAsync(string humanId, int profileNo, int id)
    {
        var existing = await this._userGeofenceRepository.GetByIdAsync(id)
            ?? throw new InvalidOperationException($"Geofence with id {id} not found.");

        if (existing.HumanId != humanId)
        {
            throw new UnauthorizedAccessException("Geofence does not belong to this user.");
        }

        // Deserialize polygon to remove from Koji
        var polygon = JsonSerializer.Deserialize<double[][]>(existing.PolygonJson) ?? [];
        await this._kojiService.RemoveGeofenceFromProjectAsync(existing.GeofenceName, polygon);

        // Remove geofence name from user's humans.area JSON array
        await this.RemoveAreaFromHumanAsync(humanId, profileNo, existing.GeofenceName);

        // Delete local tracking record
        await this._userGeofenceRepository.DeleteAsync(id);

        // Reload Poracle geofences
        await this.ReloadGeofencesSafeAsync();

        this._logger.LogInformation("Deleted custom geofence '{GeofenceName}' for user {HumanId}", existing.GeofenceName, humanId);
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
        if (!areas.Contains(geofenceName))
        {
            areas.Add(geofenceName);
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
        areas.Remove(geofenceName);

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

    private static string GenerateSlug(string displayName)
    {
        var slug = displayName.ToLowerInvariant().Replace(' ', '_');
        slug = SlugRegex().Replace(slug, string.Empty);

        if (slug.Length > MaxSlugLength)
        {
            slug = slug[..MaxSlugLength];
        }

        return slug;
    }

    [GeneratedRegex("[^a-z0-9_]")]
    private static partial Regex SlugRegex();
}
