using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// Proxies pokestop-event alarm CRUD to PoracleNG's <c>/api/v2/humans/{id}/tracking/incident</c>.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IPoracleTrackingProxy"/> because the route is v2-only — there is no v1
/// <c>incident</c> endpoint, and the v2 wire shape is a different contract: named envelopes
/// (<c>{rules}</c>, <c>{created,updated,unchanged}</c>, <c>{deleted}</c>), the clean bitmask split
/// into three booleans, and a strict rule object rather than a stored row. Bending the v1 proxy to
/// serve both would have meant a second shape inside every one of its methods.
/// </para>
/// <para>
/// Incident rows live in the same <c>invasion</c> table as invasion rows, but PoracleNG filters each
/// endpoint to its own rows on every read path — list, get-by-uid, delete and bulk delete — so a uid
/// from one type is invisible to the other. Verified against 5.2.1: a bulk delete naming an invasion
/// uid alongside two event uids deleted the two and silently skipped the third.
/// </para>
/// </remarks>
public interface IPoracleIncidentProxy
{
    /// <summary>The user's pokestop-event alarms on their active profile.</summary>
    public Task<IReadOnlyList<PokestopEvent>> GetByUserAsync(string userId);

    /// <summary>
    /// Creates or updates rules. PoracleNG diffs each rule against the stored ones like any invasion row:
    /// identical is <c>unchanged</c> (uid 0), one updatable field away <em>updates</em> that stored rule
    /// (a takeover), anything else is <c>created</c>. With <paramref name="dryRun"/> nothing is written and
    /// the answer says which of the three each rule would be.
    /// </summary>
    /// <remarks>
    /// Override areas are authorised through <see cref="IUserAreaPolicy"/> first and the write is then sent
    /// <c>trusted</c>, because PoracleNG's own check allows only <c>userSelectable</c> areas and a user's
    /// own drawn fence is not one.
    /// </remarks>
    public Task<PokestopEventWriteResult> CreateAsync(string userId, IEnumerable<PokestopEvent> rules, bool dryRun = false);

    /// <summary>
    /// <see cref="CreateAsync(string, IEnumerable{PokestopEvent}, bool)"/> on profile <paramref name="profileNo"/>
    /// (<c>?profile=N</c>) rather than the active one. Only profile import uses it.
    /// </summary>
    public Task<PokestopEventWriteResult> CreateAsync(string userId, IEnumerable<PokestopEvent> rules, int profileNo, bool dryRun);

    /// <summary>
    /// Full-replaces one rule. The engine is delete-then-insert, so on 5.2.x the replacement gets a NEW uid.
    /// Since jfberry/PoracleNG #303 the uid is kept, but a failed reassignment still returns a new one, so
    /// the result is always read. PoracleNG answers 409 when the replacement would duplicate another rule.
    /// Override areas are authorised as on a create, allowing the ones this rule already holds.
    /// </summary>
    public Task<PokestopEventWriteResult> ReplaceAsync(string userId, int uid, PokestopEvent rule);

    /// <summary>
    /// <see cref="ReplaceAsync(string, int, PokestopEvent)"/> on profile <paramref name="profileNo"/>: v2 finds a
    /// uid on the addressed profile only, so a rule on a profile other than the active one is reached this way.
    /// </summary>
    public Task<PokestopEventWriteResult> ReplaceAsync(string userId, int uid, PokestopEvent rule, int profileNo);

    /// <summary>Deletes one rule by uid.</summary>
    public Task DeleteByUidAsync(string userId, int uid);

    /// <summary>
    /// Deletes several rules by uid, named in the request body (<c>{"uids":[...]}</c>, PoracleNG 5.4.0).
    /// Returns how many PoracleNG actually removed.
    /// </summary>
    public Task<int> BulkDeleteByUidsAsync(string userId, IEnumerable<int> uids);
}

/// <summary>
/// PoracleNG's answer to a v2 incident write: what it inserted, what it rewrote, and what it found
/// already present.
/// </summary>
/// <param name="Previous">
/// What each <see cref="Updated"/> rule held before this write, index for index, as PoracleNG reports it
/// under <c>previous</c>. An entry is null where PoracleNG reported no previous state for that rule.
/// </param>
public sealed record PokestopEventWriteResult(
    IReadOnlyList<PokestopEvent> Created,
    IReadOnlyList<PokestopEvent> Updated,
    IReadOnlyList<PokestopEvent> Unchanged,
    IReadOnlyList<PokestopEvent?>? Previous = null)
{
    /// <summary>
    /// The uid the rule now lives under, or null when PoracleNG named none.
    /// </summary>
    /// <remarks>
    /// An unchanged rule comes back with uid 0 — PoracleNG wrote nothing and named nothing, so there
    /// is no created resource to point at. See #459 for what answering 201 with a /0 location does.
    /// </remarks>
    public int? PrimaryUid =>
        this.Created.Count > 0 ? this.Created[0].Uid
        : this.Updated.Count > 0 ? this.Updated[0].Uid
        : null;

    /// <summary>The submission matched an existing rule exactly, so nothing was written.</summary>
    public bool WasAlreadyPresent => this.Created.Count == 0 && this.Updated.Count == 0 && this.Unchanged.Count > 0;
}
