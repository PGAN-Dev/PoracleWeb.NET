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
/// <c>weather</c> maps to nothing because PoracleWeb.NET has no weather alarms. So does
/// <c>pokestop</c>, which looks like the parent hook for lures, invasions and quests but was
/// vestigial: <c>DisablePokestop</c> appeared nowhere in the 5.1.0 processor outside the array
/// itself, and mapping it would have taken three working types away for a flag that did nothing.
/// PoracleNG 5.2.1 dropped it from the array and marked the config field deprecated
/// (jfberry/PoracleNG#197), so it now only reaches here from an older server — where ignoring it is
/// still the right answer.
/// </para>
/// <para>
/// <c>fort</c> was the mirror-image case up to 5.1.0: enforced in the processor and the bot but left
/// out of <c>hookTypes</c>, so it had to be read separately from <c>general.disable_fort_update</c>
/// on <c>GET /api/config/values</c>. The same upstream release added it to the array under the name
/// its tracking type already used, so it maps here like any other hook and the second read is now
/// made only for a server too old to report it — see <c>UpstreamFeatureFlagService.ProbeAsync</c>.
/// </para>
/// <para>
/// <c>disable_showcase</c> still has deliberately no entry: verified on a live 5.2.1, it is present in
/// <c>general</c> on <c>GET /api/config/values</c> and absent from <c>disabledHooks</c>. See
/// <c>IPoracleApiProxy.GetShowcaseDisabledAsync</c>.
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
        // Reported from PoracleNG 5.2.1 on. Older servers say so only via general.disable_fort_update.
        ["fort"] = DisableFeatureKeys.FortChanges,
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
