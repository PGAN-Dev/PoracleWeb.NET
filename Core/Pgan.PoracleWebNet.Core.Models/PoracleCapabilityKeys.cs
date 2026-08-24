namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// Optional PoracleNG features PoracleWeb offers only when the server it is pointed at can store them.
/// </summary>
/// <remarks>
/// <para>
/// PoracleNG maintains two long-lived branches and this application supports both. <c>main</c> is the
/// released line (5.1.0 at the time of writing) and is what <see cref="PoracleServerProfile.MinimumSupported"/>
/// is pinned to, so a self-hoster running a plain release gets a fully working install. <c>develop</c>
/// carries features that have not been cut into a release yet, and a self-hoster running it should get
/// those features rather than a UI written down to the older branch.
/// </para>
/// <para>
/// The alternative — branch on the version everywhere — does not work. Version answers "which release
/// line", not "can this server store this field", and those come apart constantly: a self-hoster
/// cherry-picks, a fork carries one feature and not another, and a locally built binary reports
/// <c>0.0.0</c>. Each capability therefore names the narrowest signal that actually predicts it, and
/// <see cref="PoracleCapabilityRule.Requires"/> records which, in words, for the startup log and the
/// admin page.
/// </para>
/// <para>
/// <strong>Every rule fails closed.</strong> An unreachable server, an unread migration number or an
/// unparseable version all resolve to unsupported. Hiding a control that would have worked is a
/// nuisance; showing one that silently writes a column the server does not have is the failure mode
/// <c>PoracleCompatibilityStartupService</c> exists to warn about, and the one users cannot diagnose.
/// </para>
/// <para>
/// Adding a capability means: a constant here, a rule in <see cref="All"/>, a matrix entry in
/// <c>PoracleCapabilityServiceTests</c> (the guard test fails the build otherwise), the SPA gate, and a
/// row in <c>docs/poracleng-compatibility.md</c>.
/// </para>
/// </remarks>
public static class PoracleCapabilityKeys
{
    /// <summary>
    /// Costume filtering on pokemon alarms (<c>monsters.costume</c>).
    /// </summary>
    /// <remarks>
    /// Gated on the migration rather than the version because the column is the thing that has to
    /// exist. PoracleNG's v1 handler deliberately carries no <c>diff</c> tag on <c>costume</c> and
    /// defaults it to 9000, so clients that omit it are unaffected either way — but a server without
    /// migration 6 drops the field on decode and answers 200, which is exactly the silent no-op this
    /// gate prevents.
    /// </remarks>
    public const string MonsterCostume = "monster_costume";

    /// <summary>Costume filtering on raid alarms (<c>raid.costume</c>). Separate migration to monsters.</summary>
    public const string RaidCostume = "raid_costume";

    /// <summary>
    /// Pokecoin quest rewards (<c>reward_type: 8</c>).
    /// </summary>
    /// <remarks>
    /// The one capability with neither a column nor a flag behind it: PoracleNG widened the
    /// <c>validRewardTypes</c> allowlist from <c>{2,3,4,7,12}</c> to include <c>8</c>, which leaves the
    /// version as the only available signal. Unlike costume this one is loud rather than silent — an
    /// older server answers 400 "Unrecognised reward_type value" — so the gate is about not offering a
    /// control that cannot work, not about preventing data loss.
    /// </remarks>
    public const string QuestPokecoins = "quest_pokecoins";

    /// <summary>The first PoracleNG carrying pokecoin quest rewards.</summary>
    private static readonly System.Version PokecoinsFrom = new(5, 2, 0);

    /// <summary>Migration that adds <c>monsters.costume</c>.</summary>
    private const long MonsterCostumeSchema = 6;

    /// <summary>Migration that adds <c>raid.costume</c>.</summary>
    private const long RaidCostumeSchema = 7;

    /// <summary>
    /// Every optional capability, with the rule that decides it. Enumerated rather than hand-written at
    /// each call site so the resolver, the startup log, the API response and the guard test all read the
    /// same list and cannot drift.
    /// </summary>
    public static IReadOnlyList<PoracleCapabilityRule> All
    {
        get;
    } =
    [
        new(MonsterCostume, p => p.HasSchema(MonsterCostumeSchema), "PoracleNG schema 6"),
        new(RaidCostume, p => p.HasSchema(RaidCostumeSchema), "PoracleNG schema 7"),
        new(QuestPokecoins, p => p.ParsedVersion is { } v && v >= PokecoinsFrom, "PoracleNG 5.2.0"),
    ];
}

/// <summary>
/// One optional capability and the condition under which a server has it.
/// </summary>
/// <param name="Key">The stable key the API and the SPA agree on.</param>
/// <param name="IsSupported">
/// Decides support from a probed profile. Must return false for an unknown answer, never throw, and
/// never consult anything outside the profile — the profile is the whole of what was probed.
/// </param>
/// <param name="Requires">
/// What the rule needs, phrased for a human reading a startup log or the admin page ("PoracleNG schema 6").
/// </param>
public sealed record PoracleCapabilityRule(
    string Key,
    Func<PoracleServerProfile, bool> IsSupported,
    string Requires);
