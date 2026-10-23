using System.Text.Json.Serialization;

namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>Why a stored rule needs attention after the move to PoracleNG 5.4.0's <c>/api/v2</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RuleRepairKind>))]
public enum RuleRepairKind
{
    /// <summary>An invasion rule stored with the grunt type <c>metal</c>, which the game data calls <c>steel</c>.</summary>
    MetalGrunt,

    /// <summary>A raid stored with <c>pokemon_id</c> 0, which v2 refuses; by level is <c>9000</c>.</summary>
    RaidPokemonZero,

    /// <summary>A raid v2 reads as any boss at any level, which may be stored as 9000/9000 rather than 90/9000.</summary>
    RaidAnyBossLevel9000,

    /// <summary>A max battle v2 reads as any boss at any level, which may be stored as 9000/9000.</summary>
    MaxBattleAnyBossLevel9000,

    /// <summary>A rule v2 cannot express, so it can be reported but not edited.</summary>
    CannotEdit,
}

/// <summary>One stored rule the repair scan found, with a short before and after for the admin to read.</summary>
public sealed record RuleRepairItem(
    string HumanId,
    string Type,
    int ProfileNo,
    int Uid,
    RuleRepairKind Kind,
    string Before,
    string? After,
    string? Reason);

/// <summary>Where the rule repair job is. Only one scan or apply runs at a time.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RuleRepairPhase>))]
public enum RuleRepairPhase
{
    /// <summary>Nothing has run since the process started.</summary>
    Idle,

    /// <summary>Reading every human's stored rules.</summary>
    Scanning,

    /// <summary>A scan finished; its findings can be applied.</summary>
    Scanned,

    /// <summary>Writing the repairs the scan found.</summary>
    Applying,

    /// <summary>An apply finished. Applying again re-checks every item.</summary>
    Applied,

    /// <summary>The run stopped on an error that was not about one human or one rule.</summary>
    Failed,
}

/// <summary>A human the scan could not read, and why.</summary>
public sealed record RuleRepairSkip(string HumanId, string Reason);

/// <summary>What an apply did, item by item, after each item's own dry run.</summary>
/// <param name="Repaired">Rules PoracleNG rewrote.</param>
/// <param name="AlreadyFine">Rules whose dry run answered <c>unchanged</c>, so nothing was written.</param>
/// <param name="Gone">Rules (or whole accounts) PoracleNG no longer holds.</param>
/// <param name="Conflicts">
/// Rules left as they are because the repair would duplicate another rule; <see cref="RuleRepairItem.Reason"/> says so.
/// </param>
/// <param name="Failed">Rules whose repair threw; <see cref="RuleRepairItem.Reason"/> carries the message.</param>
public sealed record RuleRepairApplyResult(
    int Repaired,
    int AlreadyFine,
    int Gone,
    IReadOnlyList<RuleRepairItem> Conflicts,
    IReadOnlyList<RuleRepairItem> Failed);

/// <summary>A snapshot of the rule repair job, safe to hand to any thread.</summary>
/// <remarks>
/// <para>
/// <see cref="Repairable"/>, <see cref="CannotEdit"/> and <see cref="Skipped"/> are what the last scan found
/// and are not changed by an apply; what an apply did lives in <see cref="Applied"/>. Scan again to see
/// what is left.
/// </para>
/// <para>
/// The three lists can run to thousands of rows. <see cref="Summary"/> drops them and keeps their counts,
/// which is what a progress poll needs.
/// </para>
/// </remarks>
public sealed record RuleRepairStatus(
    RuleRepairPhase Phase,
    int HumansTotal,
    int HumansDone,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<RuleRepairItem> Repairable,
    IReadOnlyList<RuleRepairItem> CannotEdit,
    IReadOnlyList<RuleRepairSkip> Skipped,
    RuleRepairApplyResult? Applied,
    string? Error)
{
    /// <summary>The state before anything has run.</summary>
    public static RuleRepairStatus Idle { get; } = new(RuleRepairPhase.Idle, 0, 0, null, null, [], [], [], null, null);

    /// <summary>
    /// The name of each human the three lists mention, by id, from the human list the scan read. A human
    /// with no name is left out, so the card falls back to the id. Null in a <see cref="Summary"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? HumanNames { get; init; }

    // Set only by Summary(), whose lists are empty; a full status counts its own lists.
    private int? RepairableCountKept { get; init; }

    private int? CannotEditCountKept { get; init; }

    private int? SkippedCountKept { get; init; }

    /// <summary>How many rules the scan found to repair, whether or not <see cref="Repairable"/> is included.</summary>
    public int RepairableCount => this.RepairableCountKept ?? this.Repairable.Count;

    /// <summary>How many rules the scan found that can't be edited, whether or not the list is included.</summary>
    public int CannotEditCount => this.CannotEditCountKept ?? this.CannotEdit.Count;

    /// <summary>How many humans the scan could not read, whether or not the list is included.</summary>
    public int SkippedCount => this.SkippedCountKept ?? this.Skipped.Count;

    /// <summary>
    /// This status with the three lists emptied and their counts kept, for a progress poll. A summary is
    /// for sending, never for building on: a later <c>with</c> would keep its counts beside new lists.
    /// </summary>
    public RuleRepairStatus Summary() => this with
    {
        Repairable = [],
        CannotEdit = [],
        Skipped = [],
        HumanNames = null,
        RepairableCountKept = this.RepairableCount,
        CannotEditCountKept = this.CannotEditCount,
        SkippedCountKept = this.SkippedCount,
    };
}
