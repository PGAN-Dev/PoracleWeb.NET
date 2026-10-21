using System.Text.Json.Serialization;

namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// What PoracleWeb knows about the PoracleNG instance it is pointed at.
/// </summary>
/// <remarks>
/// <para>
/// The version answers one question: is this server known to be older than the minimum this build is
/// written against. Everything that arrived after the minimum is gated on what the server publishes in
/// its own <c>/openapi.json</c> instead (<c>IPoracleV2SchemaService</c>), never on a version number.
/// </para>
/// <para>
/// Deliberately not a branch. PoracleNG stamps its branch into the binary but publishes only the
/// version on <c>/health</c>, so a develop build between releases reports the last release's number and
/// cannot be told apart. Branch would be the wrong question anyway: self-hosters run forks and
/// cherry-picks, and what matters is whether this server can serve a given request, not what it is called.
/// </para>
/// </remarks>
public sealed class PoracleServerProfile
{
    /// <summary>The oldest PoracleNG this build of PoracleWeb is written against.</summary>
    /// <remarks>
    /// <para>
    /// 5.4.0 since 2026-10-21. Every PoracleNG surface this build calls (the <c>/api/v2</c> tracking, human,
    /// profile and mute routes among them) exists on that release, so code that kept older servers working
    /// is deleted rather than gated. Before it the minimum was 5.2.1, which deleted the 5.1.0 fallbacks the
    /// same way.
    /// </para>
    /// </remarks>
    public static readonly System.Version MinimumSupported = new(5, 4, 0);

    /// <summary>Version string as reported, e.g. <c>5.4.0</c>. Null when the server could not be reached.</summary>
    public string? Version
    {
        get; init;
    }

    /// <summary>
    /// The feature map from <c>/health</c>, verbatim. Absent key means unsupported — PoracleNG's own
    /// documented contract for this map, so clients may default-false rather than probe.
    /// </summary>
    /// <remarks>
    /// It covers bot and template-editor features only; nothing in it describes alarms.
    /// </remarks>
    public IReadOnlyDictionary<string, bool> Capabilities { get; init; } = new Dictionary<string, bool>(StringComparer.Ordinal);

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

    /// <summary>
    /// True when PoracleNG answered and is not known to be older than <see cref="MinimumSupported"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one rule every version-decided feature shares: costume filters, quiet periods, Pokécoin quest
    /// rewards, moving a saved place, and whether edits and deletes try <c>/api/v2</c>. Each of those
    /// exists on 5.4.0, so on a supported server the answer is always yes.
    /// </para>
    /// <para>
    /// An unparseable version, or the <c>0.0.0</c> a locally built binary reports, counts as supported: a
    /// build of current source is newer than the minimum, and hiding features from it on a guess helps
    /// nobody. An unreachable server does not, so a control it would decide stays hidden until it answers
    /// and the v2 surfaces are not tried. Known-older servers answer false: this build is not written
    /// against them, and the startup log and the admin Versions card say so.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public bool IsSupported => this.Reachable && !this.IsBelowMinimum;

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
