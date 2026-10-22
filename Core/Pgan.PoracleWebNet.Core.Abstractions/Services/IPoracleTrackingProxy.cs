using System.Text.Json;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// Proxies alarm tracking CRUD operations to the PoracleNG REST API instead of writing
/// directly to the Poracle database. PoracleNG handles field defaults, dedup, and
/// immediate state reload on every mutation.
/// </summary>
public interface IPoracleTrackingProxy
{
    /// <summary>
    /// Fetches all tracking alarms of a type for a user on their active profile, read through v2 and
    /// restated in v1's row shape.
    /// </summary>
    public Task<JsonElement> GetByUserAsync(string type, string userId);

    /// <summary>
    /// Creates one or more tracking rules through <c>POST /api/v2/humans/{id}/tracking/{type}?silent=true</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="body"/> is one v1-shaped row (an object) or several (an array). Every row is translated
    /// and its scope checked before anything is sent; a row v2 cannot be told faithfully is refused, never sent
    /// to v1. A row naming override areas is authorised by <see cref="IUserAreaPolicy"/> and the write is then
    /// sent with <c>trusted=true</c>.
    /// </para>
    /// <para>
    /// v2's POST still merges a row one updatable field away from a stored rule into that rule, so a dry run
    /// goes first. A single row that would take another rule over is refused with
    /// <see cref="Pgan.PoracleWebNet.Core.Models.TrackingConflictException"/>; one already stored exactly is
    /// reported in <see cref="TrackingCreateResult.AlreadyPresent"/> and nothing is written. In a batch, only
    /// the rows the dry run reports as new are written, and the rest are counted.
    /// </para>
    /// </remarks>
    public Task<TrackingCreateResult> CreateAsync(string type, string userId, JsonElement body);

    /// <summary>
    /// <see cref="CreateAsync"/> onto a profile other than the active one, named by <c>&amp;profile=N</c>.
    /// </summary>
    /// <remarks>
    /// An overload rather than an optional parameter: Moq setups are expression trees, which cannot use
    /// optional arguments (CS0854), so an optional parameter would break every existing setup of
    /// <see cref="CreateAsync"/>.
    /// </remarks>
    public Task<TrackingCreateResult> CreateOnProfileAsync(string type, string userId, JsonElement body, int profileNo);

    /// <summary>
    /// Applies the same settings to several rules of one type through
    /// <c>PATCH /api/v2/humans/{id}/tracking/{type}?silent=true</c> with <c>{"uids":[...],"set":{...}}</c>.
    /// Bulk distance and the clean toggle use it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the members of <paramref name="settings"/> that are not null are sent, and PoracleNG leaves every field
    /// the request does not name as stored. Each rule keeps its uid. The uids are processed in the order
    /// given, which decides which rule wins a conflict, and each lands in exactly one of the result's lists:
    /// there is no all-or-nothing, so a refusal is an answer, not an exception.
    /// </para>
    /// <para>
    /// An empty <paramref name="uids"/> sends nothing. A server without the route (gin's plaintext 404) is
    /// <see cref="Pgan.PoracleWebNet.Core.Models.PoracleUnsupportedException"/>; a 400 or 422 for the whole
    /// request is <see cref="Pgan.PoracleWebNet.Core.Models.AlarmValidationException"/>.
    /// </para>
    /// </remarks>
    public Task<Pgan.PoracleWebNet.Core.Models.TrackingPatchResult> PatchAsync(
        string type, string userId, IReadOnlyList<int> uids, Pgan.PoracleWebNet.Core.Models.TrackingPatchSet settings);

    /// <summary>
    /// Replaces one existing tracking rule, addressed by its uid, through
    /// <c>PUT /api/v2/humans/{id}/tracking/{type}/{uid}?silent=true</c>. Nothing here writes through v1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="body"/> is a single v1-shaped alarm object, the same shape every alarm service already
    /// builds. A row v2 cannot be told faithfully is refused before anything is sent. Override areas are
    /// authorised as on a create, except that names the stored rule already holds are allowed.
    /// </para>
    /// <para>
    /// 404 when the uid is not this human's; 409, reported as the takeover refusal, when the replacement would
    /// duplicate another rule. Since jfberry/PoracleNG #303 the uid is kept, but a failed reassignment still
    /// returns a new one, so the response is always read.
    /// </para>
    /// </remarks>
    /// <returns>The uid the rule now lives under, which may differ from <paramref name="uid"/>.</returns>
    public Task<Pgan.PoracleWebNet.Core.Models.TrackingUpdateResult> UpdateByUidAsync(
        string type, string userId, int uid, System.Text.Json.JsonElement body);

    /// <summary>
    /// Deletes a single tracking alarm by UID on the active profile. Maps to
    /// <c>DELETE /api/v2/humans/{id}/tracking/{type}/{uid}?silent=true</c>; a rule PoracleNG does not hold is
    /// treated as already gone. For <c>invasion</c>, a uid the invasion route does not hold is tried as an
    /// <c>incident</c> (a Pokestop event the invasion read folded in).
    /// </summary>
    public Task DeleteByUidAsync(string type, string userId, int uid);

    /// <summary>
    /// Deletes multiple tracking alarms by UID list on the active profile. Maps to
    /// <c>DELETE /api/v2/humans/{id}/tracking/{type}?silent=true</c> with <c>{"uids":[...]}</c> in the body.
    /// Uids PoracleNG did not delete are logged, not retried, except that <c>invasion</c> sends what it left
    /// to <c>incident</c>.
    /// </summary>
    public Task BulkDeleteByUidsAsync(string type, string userId, IEnumerable<int> uids);

    /// <summary>
    /// Deletes multiple alarms of one type that sit on a specific profile, not the active one. Maps to
    /// <c>DELETE /api/v2/humans/{id}/tracking/{type}?silent=true&amp;profile=N</c> with the uids in the body.
    /// </summary>
    /// <returns>
    /// The uids PoracleNG reports deleted. It silently skips a uid that is not on that profile, so a
    /// shorter list than was asked for is an answer, not an error.
    /// </returns>
    public Task<IReadOnlyList<int>> BulkDeleteByUidsOnProfileAsync(string type, string userId, IReadOnlyList<int> uids, int profileNo);

    /// <summary>
    /// Fetches all tracking alarms across all types for a user on their active profile, read through v2's
    /// snapshot and keyed as v1's <c>all</c> answer was.
    /// </summary>
    public Task<JsonElement> GetAllTrackingAsync(string userId);

    /// <summary>
    /// Fetches all tracking rules across all profiles for a user, read through v2's snapshot with
    /// <c>?all_profiles=true</c>.
    /// </summary>
    public Task<JsonElement> GetAllTrackingAllProfilesAsync(string userId);

    /// <summary>
    /// Triggers a state reload in PoracleNG.
    /// Maps to GET /api/reload
    /// </summary>
    public Task ReloadStateAsync();
}

/// <summary>
/// PoracleNG's answer to a tracking create. It reports exactly what it did; PoracleWeb used to read
/// almost none of it, which is the shared root of #459, #462, #463, #468 and #469.
/// </summary>
/// <param name="NewUids">The uids PoracleNG created.</param>
/// <param name="AlreadyPresent">Rows already stored exactly, so not written.</param>
/// <param name="Updates">Rows that updated a stored rule. Always 0 from a v2 create, which never lets a row take one over.</param>
/// <param name="Inserts">How many rules were created.</param>
/// <param name="RowUids">
/// One entry per submitted row, in submission order: the uid created for it, or 0 when nothing was created for
/// that row (already present, skipped as a takeover, or the commit could not be paired back to it).
/// </param>
/// <param name="TakeoversSkipped">Rows of a batch left out because they would have taken another rule over.</param>
public record TrackingCreateResult(
    List<long> NewUids,
    int AlreadyPresent,
    int Updates,
    int Inserts,
    IReadOnlyList<int>? RowUids = null,
    int TakeoversSkipped = 0)
{
    /// <inheritdoc cref="TrackingCreateResult" path="/param[@name='RowUids']"/>
    public IReadOnlyList<int> RowUids { get; init; } = RowUids ?? [];

    /// <summary>The uid the row now lives under, or <c>null</c> when PoracleNG named none.</summary>
    public int? PrimaryUid => this.NewUids.Count > 0 ? (int)this.NewUids[0] : null;

    /// <summary>
    /// PoracleNG wrote no new row: it either matched an existing one or found the submission
    /// already present. On a create that means the alarm was NOT created by this call.
    /// </summary>
    public bool InsertedNothing => this.Inserts == 0;

    /// <summary>The submission duplicated an existing row exactly, so nothing was named or written.</summary>
    public bool WasRejectedAsDuplicate => this.AlreadyPresent > 0 && this.Inserts == 0 && this.NewUids.Count == 0;
}
