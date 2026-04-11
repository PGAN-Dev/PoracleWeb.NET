using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Data;

namespace Pgan.PoracleWebNet.Core.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IUserAreaDualWriter"/>. Every method mutates tracked
/// entities in-memory and calls <c>SaveChangesAsync</c> exactly once, which EF Core wraps in a
/// single implicit DB transaction — so <c>humans.area</c> and <c>profiles.area</c> cannot drift.
/// </summary>
public class UserAreaDualWriter(PoracleContext context) : IUserAreaDualWriter
{
    private readonly PoracleContext _context = context;

    public async Task<bool> AddAreaToActiveProfileAsync(string humanId, string areaName)
    {
        var lowerName = areaName.ToLowerInvariant();

        var human = await this._context.Humans.FirstOrDefaultAsync(h => h.Id == humanId)
            ?? throw new InvalidOperationException($"Human with id {humanId} not found.");

        var humanAreas = ParseAreas(human.Area);
        var humanChanged = false;
        if (!humanAreas.Contains(lowerName))
        {
            humanAreas.Add(lowerName);
            human.Area = JsonSerializer.Serialize(humanAreas);
            humanChanged = true;
        }

        var profile = await this._context.Profiles
            .FirstOrDefaultAsync(p => p.Id == humanId && p.ProfileNo == human.CurrentProfileNo);
        var profileChanged = false;
        if (profile is not null)
        {
            var profileAreas = ParseAreas(profile.Area);
            if (!profileAreas.Contains(lowerName))
            {
                profileAreas.Add(lowerName);
                profile.Area = JsonSerializer.Serialize(profileAreas);
                profileChanged = true;
            }
        }

        if (humanChanged || profileChanged)
        {
            // Single SaveChangesAsync → single implicit transaction → atomic dual-write.
            await this._context.SaveChangesAsync();
            return true;
        }

        return false;
    }

    public async Task<bool> RemoveAreaFromActiveProfileAsync(string humanId, string areaName)
    {
        var lowerName = areaName.ToLowerInvariant();

        var human = await this._context.Humans.FirstOrDefaultAsync(h => h.Id == humanId)
            ?? throw new InvalidOperationException($"Human with id {humanId} not found.");

        var humanAreas = ParseAreas(human.Area);
        var humanChanged = humanAreas.Remove(lowerName);
        if (humanChanged)
        {
            human.Area = humanAreas.Count > 0 ? JsonSerializer.Serialize(humanAreas) : "[]";
        }

        var profile = await this._context.Profiles
            .FirstOrDefaultAsync(p => p.Id == humanId && p.ProfileNo == human.CurrentProfileNo);
        var profileChanged = false;
        if (profile is not null)
        {
            var profileAreas = ParseAreas(profile.Area);
            if (profileAreas.Remove(lowerName))
            {
                profile.Area = profileAreas.Count > 0 ? JsonSerializer.Serialize(profileAreas) : "[]";
                profileChanged = true;
            }
        }

        if (humanChanged || profileChanged)
        {
            await this._context.SaveChangesAsync();
            return true;
        }

        return false;
    }

    public async Task<bool> AddAreasToActiveProfileAsync(string humanId, IReadOnlyCollection<string> areaNames)
    {
        if (areaNames.Count == 0)
        {
            return false;
        }

        // Deduplicate and lowercase once — avoids repeated allocations in the inner loops.
        var normalized = areaNames
            .Select(a => a.ToLowerInvariant())
            .Distinct()
            .ToList();

        var human = await this._context.Humans.FirstOrDefaultAsync(h => h.Id == humanId)
            ?? throw new InvalidOperationException($"Human with id {humanId} not found.");

        var humanAreas = ParseAreas(human.Area);
        var humanChanged = false;
        foreach (var name in normalized)
        {
            if (!humanAreas.Contains(name))
            {
                humanAreas.Add(name);
                humanChanged = true;
            }
        }
        if (humanChanged)
        {
            human.Area = JsonSerializer.Serialize(humanAreas);
        }

        var profile = await this._context.Profiles
            .FirstOrDefaultAsync(p => p.Id == humanId && p.ProfileNo == human.CurrentProfileNo);
        var profileChanged = false;
        if (profile is not null)
        {
            var profileAreas = ParseAreas(profile.Area);
            foreach (var name in normalized)
            {
                if (!profileAreas.Contains(name))
                {
                    profileAreas.Add(name);
                    profileChanged = true;
                }
            }
            if (profileChanged)
            {
                profile.Area = JsonSerializer.Serialize(profileAreas);
            }
        }

        if (humanChanged || profileChanged)
        {
            await this._context.SaveChangesAsync();
            return true;
        }

        return false;
    }

    public async Task<bool> RemoveAreaFromAllProfilesAsync(string humanId, string areaName)
    {
        var lowerName = areaName.ToLowerInvariant();

        var human = await this._context.Humans.FirstOrDefaultAsync(h => h.Id == humanId);
        var humanChanged = false;
        if (human is not null)
        {
            var humanAreas = ParseAreas(human.Area);
            if (humanAreas.Remove(lowerName))
            {
                human.Area = humanAreas.Count > 0 ? JsonSerializer.Serialize(humanAreas) : "[]";
                humanChanged = true;
            }
        }

        var profiles = await this._context.Profiles
            .Where(p => p.Id == humanId)
            .ToListAsync();
        var anyProfileChanged = false;
        foreach (var profile in profiles)
        {
            var profileAreas = ParseAreas(profile.Area);
            if (profileAreas.Remove(lowerName))
            {
                profile.Area = profileAreas.Count > 0 ? JsonSerializer.Serialize(profileAreas) : "[]";
                anyProfileChanged = true;
            }
        }

        if (humanChanged || anyProfileChanged)
        {
            await this._context.SaveChangesAsync();
            return true;
        }

        return false;
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
            return [.. areaJson.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        }
    }
}
