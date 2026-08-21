namespace Pgan.PoracleWebNet.Core.Models;

/// <summary>
/// Translates PoracleNG's own per-webhook-type disable flags into the <c>disable_*</c> keys this
/// application gates on, so a type an operator switched off in Poracle's <c>config.toml</c> stops
/// being offered here too.
/// </summary>
/// <remarks>
/// <para>
/// The upstream names come from the <c>hookTypes</c> list in
/// <c>processor/internal/api/config.go</c>, which is what <c>GET /api/config/poracleWeb</c> reports
/// as <c>disabledHooks</c>. PoracleNG enforces the same flags in two other places — the processor
/// drops the webhook and the bot refuses the command — so honouring them here makes the web UI
/// agree with the two surfaces that already do. See #769.
/// </para>
/// <para>
/// Two entries in the upstream array deliberately map to nothing:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>pokestop</c> looks like the parent hook for lures, invasions and quests, but
/// <c>DisablePokestop</c> appears nowhere in the PoracleNG 5.1.0 processor outside the
/// <c>disabledHooks</c> list itself. Mapping it would take three working alarm types away from any
/// server that sets a flag which currently does nothing.
/// </description></item>
/// <item><description>
/// <c>weather</c> has no counterpart because PoracleWeb has no weather alarms.
/// </description></item>
/// </list>
/// <para>
/// <c>disable_fort_update</c> is the mirror-image case: PoracleNG honours it in both the processor
/// and the bot, but omits it from the <c>hookTypes</c> list, so it never appears in
/// <c>disabledHooks</c>. It is read separately from <c>general.disable_fort_update</c> on
/// <c>GET /api/config/values</c> — see <c>IPoracleApiProxy.GetFortUpdateDisabledAsync</c>.
/// </para>
/// </remarks>
public static class PoracleDisabledHookMap
{
    /// <summary>
    /// Upstream <c>disabledHooks</c> entry → the <see cref="DisableFeatureKeys"/> value it forces off.
    /// Entries absent from this map (<c>pokestop</c>, <c>weather</c>) disable nothing.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ByHookName
    {
        get;
    } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["pokemon"] = DisableFeatureKeys.Pokemon,
        // Eggs share the raid key here for the same reason they share it everywhere else: one raid UI.
        ["raid"] = DisableFeatureKeys.Raids,
        ["quest"] = DisableFeatureKeys.Quests,
        ["invasion"] = DisableFeatureKeys.Invasions,
        ["lure"] = DisableFeatureKeys.Lures,
        ["nest"] = DisableFeatureKeys.Nests,
        ["gym"] = DisableFeatureKeys.Gyms,
        ["maxbattle"] = DisableFeatureKeys.MaxBattles,
    };

    /// <summary>
    /// Maps an upstream <c>disabledHooks</c> array to the set of <c>disable_*</c> keys it forces off.
    /// Unknown or unmapped hook names are ignored rather than guessed at.
    /// </summary>
    public static IReadOnlySet<string> ToDisableKeys(IEnumerable<string>? disabledHooks)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (disabledHooks is null)
        {
            return keys;
        }

        foreach (var hook in disabledHooks)
        {
            if (!string.IsNullOrWhiteSpace(hook) && ByHookName.TryGetValue(hook.Trim(), out var key))
            {
                keys.Add(key);
            }
        }

        return keys;
    }
}
