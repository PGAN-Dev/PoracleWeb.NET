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
    /// Creates or updates rules. PoracleNG diffs on the rule's identity — its <c>display_type</c> —
    /// so a submission naming an event the user already tracks <em>updates</em> that rule and re-keys
    /// it to a new uid rather than adding a second one. An identical resubmission is reported as
    /// unchanged with uid 0.
    /// </summary>
    public Task<PokestopEventWriteResult> CreateAsync(string userId, IEnumerable<PokestopEvent> rules);

    /// <summary>
    /// Full-replaces one rule. The engine is delete-then-insert, so the replacement gets a NEW uid.
    /// </summary>
    public Task<PokestopEventWriteResult> ReplaceAsync(string userId, int uid, PokestopEvent rule);

    /// <summary>Deletes one rule by uid.</summary>
    public Task DeleteByUidAsync(string userId, int uid);

    /// <summary>Deletes several rules by uid. Returns how many PoracleNG actually removed.</summary>
    public Task<int> BulkDeleteByUidsAsync(string userId, IEnumerable<int> uids);
}

/// <summary>
/// PoracleNG's answer to a v2 incident write: what it inserted, what it rewrote, and what it found
/// already present.
/// </summary>
public sealed record PokestopEventWriteResult(
    IReadOnlyList<PokestopEvent> Created,
    IReadOnlyList<PokestopEvent> Updated,
    IReadOnlyList<PokestopEvent> Unchanged)
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
