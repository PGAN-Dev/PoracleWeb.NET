namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// Where a tracking rule lives once an update has been applied, and which PoracleNG surface applied it.
/// </summary>
/// <remarks>
/// <para>
/// The uid matters because <c>/api/v2</c>'s full-replace PUT is delete-then-insert. On 5.2.x the
/// replacement rule is handed a NEW uid, returned under <c>updated</c>; since jfberry/PoracleNG #303 the
/// PUT keeps the original, but a failed reassignment still returns the new one, so the response is always
/// read. Anything holding the old uid — quick-pick applied state above all — has to follow it when it
/// moves. On the frozen v1 surface the uid normally stays put, so <see cref="Uid"/> simply echoes what
/// was submitted.
/// </para>
/// <para>
/// A struct on purpose: a mocked proxy that was never set up answers <c>default</c>, which is uid 0, and
/// callers already treat a uid of 0 as "PoracleNG named none" rather than dereferencing null.
/// </para>
/// </remarks>
/// <param name="Uid">The uid the rule now lives under, or 0 when PoracleNG named none.</param>
/// <param name="UsedV2">True when the write went to <c>/api/v2</c> rather than the v1 surface.</param>
public readonly record struct TrackingUpdateResult(int Uid, bool UsedV2);
