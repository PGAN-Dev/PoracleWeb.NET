using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Data;

namespace Pgan.PoracleWebNet.Core.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IAlarmOverrideAreaWriter"/>: one raw-SQL UPDATE over a table
/// name taken from a fixed map.
/// </summary>
public class AlarmOverrideAreaWriter(PoracleContext context) : IAlarmOverrideAreaWriter
{
    private readonly PoracleContext _context = context;

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
