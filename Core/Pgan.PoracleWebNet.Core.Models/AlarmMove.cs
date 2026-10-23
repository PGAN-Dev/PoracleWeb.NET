namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>How one move ended. A move never loses an alert: it creates on the target first, deletes the source second.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<MoveStatus>))]
public enum MoveStatus
{
    /// <summary>The target has the alert and the source no longer does.</summary>
    Moved,

    /// <summary>The target already holds a rule this one would take over. Nothing was written; the source is untouched.</summary>
    Conflict,

    /// <summary>The target has the alert but the source could not be deleted, so it now exists on both sides.</summary>
    SourceRemains,

    /// <summary>A check failed before anything was written. Nothing changed.</summary>
    Refused,
}

/// <param name="Uid">The alarm's uid on the side it came from.</param>
/// <param name="Status">How the move ended.</param>
/// <param name="NewUid">The uid on the target, when the move created one.</param>
/// <param name="ExistingUid">For a conflict: the target's rule that is in the way.</param>
/// <param name="Reason">A sentence the person can read; null for a clean move.</param>
public sealed record MoveOutcome(int Uid, MoveStatus Status, int? NewUid = null, int? ExistingUid = null, string? Reason = null);

/// <summary>Body of <c>POST /api/alarm-move</c>.</summary>
public sealed class AlarmMoveRequest
{
    public string Type { get; set; } = string.Empty;

    public List<int> Uids { get; set; } = [];

    public string Destination { get; set; } = string.Empty;
}
