namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// The settings a v2 tracking <c>PATCH</c> applies to every rule it names. Only the members that are not
/// null are sent; PoracleNG leaves every field the request does not name as stored, which is how the edit
/// and summary bits survive a clean toggle.
/// </summary>
/// <param name="Distance">The radius in metres; 0 matches by the profile's areas instead.</param>
/// <param name="Clean">The auto-delete bit (clean bit 1) and nothing else.</param>
public sealed record TrackingPatchSet(int? Distance = null, bool? Clean = null);

/// <summary>
/// PoracleNG's answer to a v2 tracking <c>PATCH</c>. There is no all-or-nothing: each uid lands in exactly one
/// of the three lists.
/// </summary>
/// <param name="Updated">Rules that took the new settings. PATCH keeps each rule's uid.</param>
/// <param name="Unchanged">Rules that already had them.</param>
/// <param name="Refused">Rules left alone, with PoracleNG's reason.</param>
public sealed record TrackingPatchResult(
    IReadOnlyList<int> Updated,
    IReadOnlyList<int> Unchanged,
    IReadOnlyList<PatchRefusal> Refused)
{
    /// <summary>Nothing was asked for, so nothing was done.</summary>
    public static TrackingPatchResult None { get; } = new([], [], []);
}

/// <summary>One rule a v2 tracking <c>PATCH</c> left alone.</summary>
/// <param name="Uid">The rule as it was named in the request.</param>
/// <param name="Reason">
/// <see cref="PatchRefusalReasons.Invalid"/>, <see cref="PatchRefusalReasons.Conflict"/> or
/// <see cref="PatchRefusalReasons.NotFound"/>. PoracleNG says to switch on this, never on the detail.
/// </param>
/// <param name="Detail">PoracleNG's explanation, for a log line.</param>
/// <param name="ConflictsWith">For a conflict, the rule it would have collided with.</param>
public sealed record PatchRefusal(int Uid, string Reason, string Detail, int? ConflictsWith);

/// <summary>The reasons PoracleNG gives for refusing one rule of a <c>PATCH</c>.</summary>
public static class PatchRefusalReasons
{
    /// <summary>The patched rule would fail validation (for distance, usually its scope).</summary>
    public const string Invalid = "invalid";

    /// <summary>The patched rule would duplicate another, or be one updatable field away from it.</summary>
    public const string Conflict = "conflict";

    /// <summary>Not this human's rule of this type on this profile.</summary>
    public const string NotFound = "not_found";
}
