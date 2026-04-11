using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Models.Helpers;
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

    public Task<bool> AddAreaToActiveProfileAsync(string humanId, string areaName)
    {
        // Both guards run in the wrapper so the single-item contract fails fast and gives a
        // direct error for the offending parameter (the bulk path strips blank entries from
        // its input collection rather than throwing, which is the right call there but the
        // wrong contract here — passing a blank single-item name is a programming error).
        ArgumentException.ThrowIfNullOrWhiteSpace(humanId);
        ArgumentException.ThrowIfNullOrWhiteSpace(areaName);
        // Delegate to the bulk path so there's a single read-modify-write implementation.
        return this.AddAreasToActiveProfileAsync(humanId, [areaName]);
    }

    public async Task<bool> RemoveAreaFromActiveProfileAsync(string humanId, string areaName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(humanId);
        ArgumentException.ThrowIfNullOrWhiteSpace(areaName);

        var lowerName = areaName.ToLowerInvariant();

        var human = await this._context.Humans.FirstOrDefaultAsync(h => h.Id == humanId)
            ?? throw new InvalidOperationException($"Human with id {humanId} not found.");

        var humanAreas = AreaListJson.Parse(human.Area);
        var humanChanged = humanAreas.Remove(lowerName);
        if (humanChanged)
        {
            human.Area = AreaListJson.Serialize(humanAreas);
        }

        var profile = await this._context.Profiles
            .FirstOrDefaultAsync(p => p.Id == humanId && p.ProfileNo == human.CurrentProfileNo);
        var profileChanged = false;
        if (profile is not null)
        {
            var profileAreas = AreaListJson.Parse(profile.Area);
            if (profileAreas.Remove(lowerName))
            {
                profile.Area = AreaListJson.Serialize(profileAreas);
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
        ArgumentException.ThrowIfNullOrWhiteSpace(humanId);
        ArgumentNullException.ThrowIfNull(areaNames);

        if (areaNames.Count == 0)
        {
            return false;
        }

        // Deduplicate and lowercase once. Skip blank/whitespace-only entries — they're
        // indistinguishable from "no area" and shouldn't bloat the list.
        var normalized = areaNames
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.ToLowerInvariant())
            .Distinct()
            .ToList();

        if (normalized.Count == 0)
        {
            return false;
        }

        var human = await this._context.Humans.FirstOrDefaultAsync(h => h.Id == humanId)
            ?? throw new InvalidOperationException($"Human with id {humanId} not found.");

        var humanAreas = AreaListJson.Parse(human.Area);
        // HashSet lookup turns the inner loop from O(N·M) to O(N+M). Preserves insertion
        // order in the backing list so the on-disk ordering is stable across writes.
        var humanSet = new HashSet<string>(humanAreas);
        var humanChanged = false;
        foreach (var name in normalized)
        {
            if (humanSet.Add(name))
            {
                humanAreas.Add(name);
                humanChanged = true;
            }
        }
        if (humanChanged)
        {
            human.Area = AreaListJson.Serialize(humanAreas);
        }

        var profile = await this._context.Profiles
            .FirstOrDefaultAsync(p => p.Id == humanId && p.ProfileNo == human.CurrentProfileNo);
        var profileChanged = false;
        if (profile is not null)
        {
            var profileAreas = AreaListJson.Parse(profile.Area);
            var profileSet = new HashSet<string>(profileAreas);
            foreach (var name in normalized)
            {
                if (profileSet.Add(name))
                {
                    profileAreas.Add(name);
                    profileChanged = true;
                }
            }
            if (profileChanged)
            {
                profile.Area = AreaListJson.Serialize(profileAreas);
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
        ArgumentException.ThrowIfNullOrWhiteSpace(humanId);
        ArgumentException.ThrowIfNullOrWhiteSpace(areaName);

        var lowerName = areaName.ToLowerInvariant();

        var human = await this._context.Humans.FirstOrDefaultAsync(h => h.Id == humanId);
        var humanChanged = false;
        if (human is not null)
        {
            var humanAreas = AreaListJson.Parse(human.Area);
            if (humanAreas.Remove(lowerName))
            {
                human.Area = AreaListJson.Serialize(humanAreas);
                humanChanged = true;
            }
        }

        var profiles = await this._context.Profiles
            .Where(p => p.Id == humanId)
            .ToListAsync();
        var anyProfileChanged = false;
        foreach (var profile in profiles)
        {
            var profileAreas = AreaListJson.Parse(profile.Area);
            if (profileAreas.Remove(lowerName))
            {
                profile.Area = AreaListJson.Serialize(profileAreas);
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
}
