using System.Text.Json.Serialization;

namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// What PoracleWeb knows about the PoracleNG instance it is pointed at.
/// </summary>
/// <remarks>
/// <para>
/// PoracleWeb assumed 5.1.0 and never checked, so on an older server the features that need it —
/// per-alarm scope, the PVP mega picker, the minimum time filter — wrote fields nothing stored and
/// failed silently. This is the check.
/// </para>
/// <para>
/// Deliberately not a branch. PoracleNG stamps its branch into the binary but publishes only the
/// version on <c>/health</c>, so a develop build between releases reports the last release's number and
/// cannot be told apart. Branch would be the wrong question anyway: self-hosters run forks and
/// cherry-picks, and what matters is whether this server can store a given field, not what it is called.
/// </para>
/// </remarks>
public sealed class PoracleServerProfile
{
    /// <summary>The oldest PoracleNG this build of PoracleWeb is written against.</summary>
    /// <remarks>
    /// 5.1.0 is where <c>override_location_label</c>, <c>override_areas</c> and
    /// <c>pvp_ranking_evolution</c> arrive. Below it those columns do not exist, so the controls that
    /// write them do nothing at all.
    /// </remarks>
    public static readonly System.Version MinimumSupported = new(5, 1, 0);

    /// <summary>Version string as reported, e.g. <c>5.1.0</c>. Null when the server could not be reached.</summary>
    public string? Version
    {
        get; init;
    }

    /// <summary>
    /// The feature map from <c>/health</c>, verbatim. Absent key means unsupported — PoracleNG's own
    /// documented contract for this map, so clients may default-false rather than probe.
    /// </summary>
    public IReadOnlyDictionary<string, bool> Capabilities { get; init; } = new Dictionary<string, bool>(StringComparer.Ordinal);

    /// <summary>
    /// Applied migration number from PoracleNG's <c>schema_migrations</c> table, or null when it could
    /// not be read.
    /// </summary>
    /// <remarks>
    /// The capability map covers bot and template-editor features only; nothing in it describes alarm
    /// columns. The migration number does, which is what makes it the signal for "can this server store
    /// that filter". 5.1.0 sits at 5; costume arrives at 6 and 7.
    /// </remarks>
    public long? SchemaVersion
    {
        get; init;
    }

    /// <summary>True when PoracleNG answered at all.</summary>
    public bool Reachable
    {
        get; init;
    }

    /// <summary>When this was last read from the server.</summary>
    public DateTimeOffset CheckedAt
    {
        get; init;
    }

    /// <summary>The parsed <see cref="Version"/>, or null when it is missing or unparseable.</summary>
    [JsonIgnore]
    public System.Version? ParsedVersion => TryParse(this.Version);

    /// <summary>
    /// True only when the server is known to be older than <see cref="MinimumSupported"/>.
    /// </summary>
    /// <remarks>
    /// Unreachable or unparseable is not "too old" — it is unknown, and shouting about a version nobody
    /// has established would train admins to ignore the banner. <see cref="Reachable"/> carries that
    /// case instead.
    /// </remarks>
    [JsonIgnore]
    public bool IsBelowMinimum => this.ParsedVersion is { } v && v < MinimumSupported;

    /// <summary>The profile for a server that did not answer: nothing known, nothing assumed.</summary>
    public static PoracleServerProfile Unknown(DateTimeOffset checkedAt) => new()
    {
        Reachable = false,
        CheckedAt = checkedAt,
    };

    /// <summary>
    /// True when the named capability is present and on. Missing keys are false, per PoracleNG's map
    /// contract, so a server that predates a capability behaves like one that switched it off.
    /// </summary>
    public bool Supports(string capability) =>
        this.Capabilities.TryGetValue(capability, out var enabled) && enabled;

    /// <summary>True when the applied schema is at least <paramref name="migration"/>.</summary>
    /// <remarks>Unknown schema answers false: an unread migration number must not unlock a column.</remarks>
    public bool HasSchema(long migration) => this.SchemaVersion >= migration;

    /// <summary>
    /// Parses the version PoracleNG reports. It ships "0.0.0" when the build flags are not injected, and
    /// that is not a real version — a locally built binary is treated as unknown rather than ancient.
    /// </summary>
    private static System.Version? TryParse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("0.0.0", StringComparison.Ordinal))
        {
            return null;
        }

        // Tolerate a suffix: the version may be stamped as 5.2.0-rc1 by a build script.
        var numeric = new string(raw.TakeWhile(c => char.IsAsciiDigit(c) || c == '.').ToArray()).TrimEnd('.');

        return System.Version.TryParse(numeric, out var parsed) ? parsed : null;
    }
}
