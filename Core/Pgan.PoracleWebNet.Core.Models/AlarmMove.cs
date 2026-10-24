namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>How one move ended. A move never loses an alert: it creates on the target first, deletes the source second.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(MoveStatusConverter))]
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

    /// <summary>
    /// The create failed in a way PoracleNG did not explain, so the alert may or may not exist on the target.
    /// The source was not touched. Look at both sides before trying again.
    /// </summary>
    Failed,

    /// <summary>The alarm is already on the side it was asked to go to. Nothing was written, and nothing needed to be.</summary>
    AlreadyThere,
}

/// <summary>
/// Why a move did not go ahead, as a code the SPA translates. <see cref="MoveOutcome.Reason"/> keeps an English
/// sentence for logs and older clients; nothing on screen reads it.
/// </summary>
public static class MoveReasonCodes
{
    /// <summary>The alarm is already on the target app.</summary>
    public const string AlreadyThere = "already_there";

    /// <summary>The caller's accounts are not linked (any more).</summary>
    public const string NotLinked = "not_linked";

    /// <summary>The alarm belongs to neither human of the pair.</summary>
    public const string NotYours = "not_yours";

    /// <summary>The uid was not found on either human.</summary>
    public const string NotFound = "not_found";

    /// <summary>One side has no Poracle account yet. <see cref="MoveOutcome.ReasonApp"/> names it.</summary>
    public const string NotSetUp = "not_set_up";

    /// <summary>The two humans are on different profiles.</summary>
    public const string DifferentProfiles = "different_profiles";

    /// <summary>The alarm measures from a saved place the target lacks. <see cref="MoveOutcome.ReasonPlace"/> names it.</summary>
    public const string PlaceMissing = "place_missing";

    /// <summary>The alarm is limited to an area the target may not use.</summary>
    public const string AreaNotAllowed = "area_not_allowed";

    /// <summary>PoracleNG, or validation, refused the alarm on the target.</summary>
    public const string Invalid = "invalid";

    /// <summary>The target already holds a rule this one would take over.</summary>
    public const string Conflict = "conflict";

    /// <summary>The create failed unexplained; the alarm may or may not be on the target.</summary>
    public const string Failed = "failed";

    /// <summary>Copied, but the original could not be deleted.</summary>
    public const string SourceRemains = "source_remains";
}

/// <summary>Writes a status as <c>moved</c>, <c>sourceRemains</c> and so on, whatever the host's serializer options say.</summary>
public sealed class MoveStatusConverter() : System.Text.Json.Serialization.JsonStringEnumConverter<MoveStatus>(System.Text.Json.JsonNamingPolicy.CamelCase);

/// <param name="Uid">The alarm's uid on the side it came from.</param>
/// <param name="Status">How the move ended.</param>
/// <param name="NewUid">The uid on the target, when the move created one.</param>
/// <param name="ExistingUid">For a conflict: the target's rule that is in the way.</param>
/// <param name="Reason">An English sentence for logs and older clients; null for a clean move. The SPA reads <paramref name="ReasonCode"/>.</param>
/// <param name="ReasonCode">One of <see cref="MoveReasonCodes"/>; null for a clean move.</param>
/// <param name="ReasonApp">The app a reason is about (<c>discord</c> or <c>telegram</c>), when it names one.</param>
/// <param name="ReasonPlace">The saved place a <see cref="MoveReasonCodes.PlaceMissing"/> refusal names.</param>
public sealed record MoveOutcome(
    int Uid,
    MoveStatus Status,
    int? NewUid = null,
    int? ExistingUid = null,
    string? Reason = null,
    string? ReasonCode = null,
    string? ReasonApp = null,
    string? ReasonPlace = null);

/// <summary>A move's outcome plus the alarm as it now stands on the target (null when nothing was created or matched).</summary>
/// <param name="Conflict">The refusal behind a <see cref="MoveStatus.Conflict"/>, so a single edit can hand it to the global filter.</param>
public sealed record MoveResult<TModel>(MoveOutcome Outcome, TModel? Created = default, TrackingConflictException? Conflict = null)
    where TModel : class;

/// <summary>Body of <c>POST /api/alarm-move</c>.</summary>
public sealed class AlarmMoveRequest
{
    public string Type { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.MaxLength(500)]
    public List<int> Uids { get; set; } = [];

    public string Destination { get; set; } = string.Empty;

    /// <summary>
    /// For <c>invasion</c> only: "event" or "invasion", the kind of the cards being moved. Invasion and Pokestop
    /// event rules are numbered separately, so a uid alone can name either. Absent or unknown keeps the old
    /// uid-only behaviour.
    /// </summary>
    public string? Kind { get; set; }
}
