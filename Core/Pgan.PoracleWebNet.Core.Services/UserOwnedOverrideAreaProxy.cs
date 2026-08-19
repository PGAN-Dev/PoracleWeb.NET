using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Lets an alarm confine itself to a geofence the user drew themselves.
/// </summary>
/// <remarks>
/// <para>
/// PoracleNG validates every entry of <c>override_areas</c> against <c>GetAvailableAreas</c>, which
/// filters on <c>userSelectable</c> for non-admins. PoracleWeb serves user-drawn geofences with
/// <c>userSelectable: false</c> on purpose, to keep them out of the bot's <c>!area</c> picker, so
/// submitting one is refused with 400 "area not permitted" and the whole write fails. Unlike
/// <c>setAreas</c>, which strips silently, this one rejects.
/// </para>
/// <para>
/// Matching never consults <c>userSelectable</c> — <c>resolveOverride</c> hands the rule's areas
/// straight to <c>areaOverlap</c>, a name comparison against the fences the spawn fell in. So the fix
/// is to send PoracleNG only the names it will accept, then write the full list into the row
/// afterwards. Verified against PoracleNG 5.1.0.
/// </para>
/// <para>
/// This sits as a decorator over the tracking proxy rather than inside the ten alarm services because
/// the outbound body already carries everything the decision needs, and a service-layer version would
/// mean ten new constructor parameters and the same logic repeated ten times. See #730 and the
/// per-alarm scope proposal.
/// </para>
/// <para>
/// HACK: trusted-set-areas — remove this whole class if PoracleNG grows a trusted override write.
/// </para>
/// </remarks>
public partial class UserOwnedOverrideAreaProxy(
    IPoracleTrackingProxy inner,
    IUserGeofenceRepository geofences,
    IUserAreaDualWriter areaWriter,
    ILogger<UserOwnedOverrideAreaProxy> logger) : IPoracleTrackingProxy
{
    private readonly IPoracleTrackingProxy _inner = inner;
    private readonly IUserGeofenceRepository _geofences = geofences;
    private readonly IUserAreaDualWriter _areaWriter = areaWriter;
    private readonly ILogger<UserOwnedOverrideAreaProxy> _logger = logger;

    public Task<JsonElement> GetByUserAsync(string type, string userId) =>
        this._inner.GetByUserAsync(type, userId);

    public Task DeleteByUidAsync(string type, string userId, int uid) =>
        this._inner.DeleteByUidAsync(type, userId, uid);

    public Task BulkDeleteByUidsAsync(string type, string userId, IEnumerable<int> uids) =>
        this._inner.BulkDeleteByUidsAsync(type, userId, uids);

    public Task<JsonElement> GetAllTrackingAsync(string userId) =>
        this._inner.GetAllTrackingAsync(userId);

    public Task<JsonElement> GetAllTrackingAllProfilesAsync(string userId) =>
        this._inner.GetAllTrackingAllProfilesAsync(userId);

    public Task ReloadStateAsync() => this._inner.ReloadStateAsync();

    public async Task<TrackingCreateResult> CreateAsync(string type, string userId, JsonElement body)
    {
        // Refuse an incoherent scope before anything is written. PoracleNG enforces the same three rules
        // in validateOverrideFields and answers 400, but it only sees the sanitised body — a row whose
        // only areas were the user's own would arrive with no override_areas at all and the
        // areas-versus-distance rule would not fire there. Checking here also covers the callers that
        // never touch a controller, which is where profile import and quick-pick apply slipped past their
        // guards before (#548, #565).
        EnsureScopeIsCoherent(body);

        // Fast path. Almost every write carries no override at all, and this must not add a geofence
        // query to each of them.
        if (!MentionsAnyOverrideArea(body))
        {
            return await this._inner.CreateAsync(type, userId, body);
        }

        var owned = await this.OwnedGeofenceNamesAsync(userId);
        if (owned.Count == 0)
        {
            return await this._inner.CreateAsync(type, userId, body);
        }

        var rows = RowsOf(body).ToList();
        var fullLists = rows
            .Select(r => OverrideAreasOf(r))
            .ToList();

        // Only the rows that actually name one of this user's own geofences need the workaround.
        var needsWriteBack = fullLists
            .Select(list => list is not null && list.Any(a => owned.Contains(a)))
            .ToList();

        if (!needsWriteBack.Contains(true))
        {
            return await this._inner.CreateAsync(type, userId, body);
        }

        var sanitised = StripOwned(body, owned);
        var result = await this._inner.CreateAsync(type, userId, sanitised);

        var uids = await this.ResolveUidsAsync(type, userId, sanitised, result);

        for (var i = 0; i < rows.Count; i++)
        {
            if (!needsWriteBack[i] || uids[i] is not int uid)
            {
                continue;
            }

            var written = await this._areaWriter.SetAlarmOverrideAreasAsync(userId, type, uid, fullLists[i]!);
            if (!written)
            {
                // The row PoracleNG just reported is not there to write to. Refusing loudly beats an
                // alarm that silently alerts on the whole profile instead of one small geofence.
                LogWriteBackMissedRow(this._logger, type, uid, userId);
                throw new InvalidOperationException(
                    $"Could not apply the area restriction to the {type} alarm that was just saved.");
            }
        }

        // PoracleNG reloads its state on its own mutations, and a direct column write is not one.
        await this._inner.ReloadStateAsync();

        return result;
    }

    /// <summary>
    /// The three mutual-exclusion rules PoracleNG applies to a per-alarm scope, mirrored so the refusal
    /// arrives before any write and with wording a person can act on.
    /// </summary>
    /// <remarks>
    /// A place and a set of areas are two different answers to the same question, so a row cannot carry
    /// both. A place is an anchor for a radius, so it needs one. Areas replace the radius entirely, so
    /// they cannot coexist with one.
    /// </remarks>
    private static void EnsureScopeIsCoherent(JsonElement body)
    {
        foreach (var row in RowsOf(body))
        {
            var label = row.TryGetProperty("override_location_label", out var l)
                && l.ValueKind == JsonValueKind.String
                    ? l.GetString()
                    : null;
            var hasLabel = !string.IsNullOrWhiteSpace(label);
            var hasAreas = OverrideAreasOf(row) is { Count: > 0 };
            var distance = row.TryGetProperty("distance", out var d) && d.ValueKind == JsonValueKind.Number
                ? d.GetInt32()
                : 0;

            if (hasLabel && hasAreas)
            {
                throw new AlarmValidationException(
                    "An alarm can be limited to a place or to areas, not both.");
            }

            if (hasAreas && distance > 0)
            {
                throw new AlarmValidationException(
                    "An alarm limited to areas cannot also have a radius. Clear one of them.");
            }

            if (hasLabel && distance == 0)
            {
                throw new AlarmValidationException(
                    "An alarm measured from a place needs a radius.");
            }
        }
    }

    /// <summary>The lowercase names of every geofence this user drew.</summary>
    private async Task<HashSet<string>> OwnedGeofenceNamesAsync(string userId)
    {
        var owned = await this._geofences.GetByHumanIdAsync(userId);
        return owned
            .Select(g => g.KojiName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// The uid each submitted row ended up under. Single-row writes read it from the response;
    /// batches re-read and pair on content, because PoracleNG returns <c>newUids</c> in its own order
    /// and index-pairing a batch response has bitten this codebase before (see BulkUidRemap, #443).
    /// </summary>
    private async Task<List<int?>> ResolveUidsAsync(
        string type, string userId, JsonElement submitted, TrackingCreateResult result)
    {
        var rows = RowsOf(submitted).ToList();

        if (rows.Count == 1)
        {
            return [result.PrimaryUid ?? UidOf(rows[0])];
        }

        var stored = await this._inner.GetByUserAsync(type, userId);
        var byIdentity = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in RowsOf(stored))
        {
            if (UidOf(row) is int uid)
            {
                byIdentity[IdentityOf(row)] = uid;
            }
        }

        return rows
            .Select(r => byIdentity.TryGetValue(IdentityOf(r), out var uid) ? uid : UidOf(r))
            .ToList();
    }

    /// <summary>
    /// Everything about a row that distinguishes it from another, ignoring what PoracleNG assigns and
    /// what this class rewrote. <c>override_areas</c> is excluded because the submitted row and the
    /// stored row deliberately disagree on it at this point.
    /// </summary>
    private static string IdentityOf(JsonElement row) =>
        string.Join(
            '|',
            row.EnumerateObject()
                .Where(p => p.Name is not ("uid" or "id" or "profile_no" or "description"
                    or "ping" or "override_areas"))
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => $"{p.Name}={p.Value}"));

    private static IEnumerable<JsonElement> RowsOf(JsonElement body) =>
        body.ValueKind switch
        {
            JsonValueKind.Array => body.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object),
            JsonValueKind.Object => [body],
            _ => [],
        };

    private static int? UidOf(JsonElement row) =>
        row.TryGetProperty("uid", out var uid) && uid.ValueKind == JsonValueKind.Number
            ? uid.GetInt32()
            : null;

    private static List<string>? OverrideAreasOf(JsonElement row) =>
        row.TryGetProperty("override_areas", out var areas) && areas.ValueKind == JsonValueKind.Array
            ? areas.EnumerateArray()
                .Where(a => a.ValueKind == JsonValueKind.String)
                .Select(a => a.GetString()!.ToLowerInvariant())
                .ToList()
            : null;

    private static bool MentionsAnyOverrideArea(JsonElement body) =>
        RowsOf(body).Any(r => OverrideAreasOf(r) is { Count: > 0 });

    /// <summary>
    /// The same body with the user's own geofence names removed from every <c>override_areas</c>.
    /// A list left empty drops the property, so PoracleNG sees no override rather than an empty one.
    /// </summary>
    private static JsonElement StripOwned(JsonElement body, HashSet<string> owned)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            var isArray = body.ValueKind == JsonValueKind.Array;
            if (isArray)
            {
                writer.WriteStartArray();
            }

            foreach (var row in RowsOf(body))
            {
                WriteStripped(writer, row, owned);
            }

            if (isArray)
            {
                writer.WriteEndArray();
            }
        }

        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    private static void WriteStripped(Utf8JsonWriter writer, JsonElement row, HashSet<string> owned)
    {
        writer.WriteStartObject();
        foreach (var prop in row.EnumerateObject())
        {
            if (!prop.NameEquals("override_areas") || prop.Value.ValueKind != JsonValueKind.Array)
            {
                prop.WriteTo(writer);
                continue;
            }

            var permitted = prop.Value.EnumerateArray()
                .Where(a => a.ValueKind == JsonValueKind.String
                    && !owned.Contains(a.GetString()!.ToLowerInvariant()))
                .Select(a => a.GetString()!)
                .ToList();

            if (permitted.Count == 0)
            {
                continue;
            }

            writer.WriteStartArray(prop.Name);
            foreach (var area in permitted)
            {
                writer.WriteStringValue(area);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "PoracleNG reported a {TrackingType} alarm at uid {Uid} for {UserId}, but no such row was there to apply the area restriction to.")]
    private static partial void LogWriteBackMissedRow(ILogger logger, string trackingType, int uid, string userId);
}
