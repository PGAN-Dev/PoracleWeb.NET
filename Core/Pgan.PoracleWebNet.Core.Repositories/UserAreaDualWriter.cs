using System.Text.Json;
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

    /// <summary>
    /// Removes every entry in <paramref name="list"/> that matches <paramref name="lowerName"/>
    /// case-insensitively. Returns whether any were removed. Defensive against the DB ever
    /// holding mixed-case area names — see the OrdinalIgnoreCase comment on the Add path.
    /// </summary>
    private static bool RemoveCaseInsensitive(List<string> list, string lowerName) =>
        list.RemoveAll(a => string.Equals(a, lowerName, StringComparison.OrdinalIgnoreCase)) > 0;

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
        var humanChanged = RemoveCaseInsensitive(humanAreas, lowerName);
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
            if (RemoveCaseInsensitive(profileAreas, lowerName))
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
        // OrdinalIgnoreCase is defensive — the DB convention guarantees lowercase, but if
        // a row is ever written with mixed case (manual DB poke, future PoracleJS change)
        // we still dedupe correctly instead of producing duplicate "Downtown"/"downtown" entries.
        var humanSet = new HashSet<string>(humanAreas, StringComparer.OrdinalIgnoreCase);
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
            var profileSet = new HashSet<string>(profileAreas, StringComparer.OrdinalIgnoreCase);
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
            if (RemoveCaseInsensitive(humanAreas, lowerName))
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
            if (RemoveCaseInsensitive(profileAreas, lowerName))
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

    public async Task<bool> RenameAreaInAllProfilesAsync(string humanId, string oldName, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(humanId);
        ArgumentException.ThrowIfNullOrWhiteSpace(oldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        var lowerOld = oldName.ToLowerInvariant();
        var lowerNew = newName.ToLowerInvariant();

        if (string.Equals(lowerOld, lowerNew, StringComparison.Ordinal))
        {
            return false;
        }

        var human = await this._context.Humans.FirstOrDefaultAsync(h => h.Id == humanId);
        var humanChanged = false;
        if (human is not null)
        {
            var humanAreas = AreaListJson.Parse(human.Area);
            if (RenameCaseInsensitive(humanAreas, lowerOld, lowerNew))
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
            if (RenameCaseInsensitive(profileAreas, lowerOld, lowerNew))
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

    /// <summary>
    /// Swaps <paramref name="lowerOld"/> for <paramref name="lowerNew"/> in place. A row that does not
    /// hold the old name is left alone, which is what preserves per-profile activation. If the new name
    /// is somehow already present the old one is just dropped, so the rename cannot produce a duplicate.
    /// </summary>
    private static bool RenameCaseInsensitive(List<string> list, string lowerOld, string lowerNew)
    {
        var index = list.FindIndex(a => string.Equals(a, lowerOld, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return false;
        }

        if (list.Any(a => string.Equals(a, lowerNew, StringComparison.OrdinalIgnoreCase)))
        {
            list.RemoveAt(index);
        }
        else
        {
            list[index] = lowerNew;
        }

        return true;
    }

    /// <summary>
    /// The Poracle table behind each tracking type, transcribed from PoracleNG migration
    /// <c>000004_per_rule_overrides</c>. Four of the ten are not the type name: pokemon lives in
    /// <c>monsters</c>, lure in <c>lures</c>, nest in <c>nests</c>, fort in <c>forts</c>.
    /// </summary>
    /// <remarks>
    /// A table name cannot be a SQL parameter, so it is interpolated — which is safe only because it
    /// comes from this fixed map and an unknown key throws rather than falling through to the caller's
    /// string. Never widen this to accept a caller-supplied table.
    /// </remarks>
    private static readonly Dictionary<string, string> AlarmTables = new(StringComparer.Ordinal)
    {
        ["pokemon"] = "monsters",
        ["raid"] = "raid",
        ["egg"] = "egg",
        ["quest"] = "quest",
        ["invasion"] = "invasion",
        ["lure"] = "lures",
        ["nest"] = "nests",
        ["gym"] = "gym",
        ["fort"] = "forts",
        ["maxbattle"] = "maxbattle",
    };

    public async Task<bool> SetAlarmOverrideAreasAsync(
        string humanId, string trackingType, int uid, IReadOnlyCollection<string> areaNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(humanId);

        if (!AlarmTables.TryGetValue(trackingType, out var table))
        {
            throw new ArgumentOutOfRangeException(
                nameof(trackingType), trackingType, "Not a PoracleNG tracking type.");
        }

        // Lowercase with spaces, matching parseOverrideAreas in PoracleNG and the geofence naming
        // convention. Blank entries are dropped rather than stored as empty strings, which would match
        // no fence and read as a corrupt list.
        var normalized = areaNames
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Replace('_', ' ').ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // NULL, not "[]" — marshalOverrideAreas stores nil for an empty list, and parseOverrideAreas
        // reads "" back as no override. Writing "[]" would be a list that matches nothing.
        object? value = normalized.Count == 0 ? null : JsonSerializer.Serialize(normalized);

        // Raw SQL, and deliberately unquoted identifiers: the repository tests run on SQLite, which
        // rejects MySQL backticks. Scoped by id as well as uid so a mis-supplied uid cannot reach
        // another user's alarm.
        var rows = await this._context.Database.ExecuteSqlRawAsync(
            $"UPDATE {table} SET override_areas = {{0}} WHERE id = {{1}} AND uid = {{2}}",
            value ?? DBNull.Value,
            humanId,
            uid);

        return rows > 0;
    }
}
