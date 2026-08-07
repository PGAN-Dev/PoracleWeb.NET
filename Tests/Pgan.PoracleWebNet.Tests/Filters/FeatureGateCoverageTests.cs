using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Filters;

/// <summary>
/// Guards the bug class where a <c>disable_*</c> toggle existed only in the SPA: the nav item was hidden
/// but the API stayed open, so anyone calling it directly — or tampering with client state — kept full use
/// of a feature the operator had switched off. <c>disable_areas</c>, <c>disable_profiles</c> and
/// <c>disable_location</c> all shipped that way.
/// <para>
/// A toggle whose only enforcement is client-side is decoration, not a gate. These tests fail if a key is
/// added to <see cref="DisableFeatureKeys"/> without a controller actually enforcing it.
/// </para>
/// </summary>
public class FeatureGateCoverageTests
{
    private static readonly Assembly ApiAssembly = typeof(AreaController).Assembly;

    /// <summary>Every <c>disable_*</c> constant declared on <see cref="DisableFeatureKeys"/>.</summary>
    public static TheoryData<string> AllDisableKeys
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var key in DeclaredKeys())
            {
                data.Add(key);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AllDisableKeys))]
    public void EveryDisableKeyIsEnforcedByAtLeastOneController(string disableKey)
    {
        var enforced = GatedControllers()
            .Any(c => string.Equals(c.key, disableKey, StringComparison.Ordinal));

        Assert.True(
            enforced,
            $"'{disableKey}' is declared in DisableFeatureKeys but no controller carries "
            + $"[RequireFeatureEnabled(\"{disableKey}\")]. A toggle enforced only in the SPA is not a gate — "
            + "the endpoints stay reachable by direct API call.");
    }

    [Theory]
    [InlineData(typeof(AreaController), "disable_areas")]
    [InlineData(typeof(ProfileController), "disable_profiles")]
    [InlineData(typeof(ProfileOverviewController), "disable_profiles")]
    [InlineData(typeof(LocationController), "disable_location")]
    [InlineData(typeof(MonsterController), "disable_mons")]
    // Gated per-action rather than per-controller on purpose: reads stay open so existing
    // geofences keep being served while creating new ones is blocked.
    [InlineData(typeof(UserGeofenceController), "disable_user_geofences")]
    public void ControllerCarriesTheExpectedGate(Type controller, string expectedKey) =>
        Assert.Contains(expectedKey, KeysEnforcedBy(controller));

    /// <summary>
    /// The gates read their key from <see cref="DisableFeatureKeys"/>, so a controller referencing a
    /// literal that drifted from the constants would silently never fire.
    /// </summary>
    [Fact]
    public void NoControllerGatesOnAnUndeclaredKey()
    {
        var declared = DeclaredKeys().ToHashSet(StringComparer.Ordinal);

        var unknown = GatedControllers()
            .Where(c => !declared.Contains(c.key))
            .Select(c => $"{c.controller.Name} -> '{c.key}'")
            .ToList();

        Assert.True(unknown.Count == 0, "Controllers gate on keys absent from DisableFeatureKeys: " + string.Join(", ", unknown));
    }

    private static IEnumerable<string> DeclaredKeys() =>
        typeof(DisableFeatureKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Distinct(StringComparer.Ordinal);

    /// <summary>
    /// Keys a controller enforces, whether the attribute sits on the class or on individual actions —
    /// UserGeofenceController gates per-action so its read endpoints stay open.
    /// </summary>
    private static List<string> KeysEnforcedBy(Type controller)
    {
        var attributes = controller.GetCustomAttributes<RequireFeatureEnabledAttribute>(inherit: true)
            .Concat(controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(m => m.GetCustomAttributes<RequireFeatureEnabledAttribute>(inherit: true)));

        return [.. attributes
            .Where(a => a.Arguments is { Length: > 0 })
            .Select(a => (string)a.Arguments![0])
            .Distinct(StringComparer.Ordinal)];
    }

    private static List<(Type controller, string key)> GatedControllers() =>
        [.. ApiAssembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => KeysEnforcedBy(t).Select(k => (controller: t, key: k)))];
}
