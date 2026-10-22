using System.Text.RegularExpressions;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Models;

/// <summary>
/// <see cref="InvasionGruntTypes"/> is the backend twin of the <c>GRUNT_TYPES</c> table in the Angular
/// invasion dialog. Both drive a "track everything" fan-out, and PoracleNG has no catch-all to fall
/// back on, so a one-sided edit silently leaves one path tracking fewer grunts than the other. These
/// tests read the TypeScript and compare. See #416.
/// </summary>
public class InvasionGruntTypesTests
{
    private const string DialogPath =
        "Applications/Pgan.PoracleWebNet.App/ClientApp/src/app/modules/invasions/invasion-add-dialog.component.ts";

    [Fact]
    public void AllIsTheUnionOfItsParts()
    {
        Assert.Equal(
            [.. InvasionGruntTypes.Elemental, .. InvasionGruntTypes.Special, .. InvasionGruntTypes.Leaders, InvasionGruntTypes.Giovanni],
            InvasionGruntTypes.All);
    }

    [Fact]
    public void AllHasNoDuplicates()
    {
        Assert.Equal(InvasionGruntTypes.All.Count, InvasionGruntTypes.All.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AllIsLowercase()
    {
        // grunt_type is matched verbatim upstream; a capitalised entry would just never fire.
        Assert.All(InvasionGruntTypes.All, t => Assert.Equal(t.ToLowerInvariant(), t));
    }

    [Fact]
    public void PokestopEventTypesAreExcluded()
    {
        // Kecleon, gold stops and showcases are not Team Rocket invasions. "Track all invasions"
        // must not silently subscribe a user to them.
        Assert.DoesNotContain("kecleon", InvasionGruntTypes.All);
        Assert.DoesNotContain("gold-stop", InvasionGruntTypes.All);
        Assert.DoesNotContain("showcase", InvasionGruntTypes.All);
    }

    /// <summary>
    /// The steel grunt was saved as <c>metal</c>, a name the game data does not have: on v1 it matched no
    /// invasion, and v2 refuses it outright. The "All Invasions" fan-out must send the name PoracleNG knows.
    /// </summary>
    [Fact]
    public void AllInvasionsFanOutHasNoMetalHasSteel()
    {
        Assert.DoesNotContain("metal", InvasionGruntTypes.All);
        Assert.Contains("steel", InvasionGruntTypes.All);
    }

    /// <summary>
    /// The <c>grunt_type</c> values the PoracleNG 5.4.0 bed (dev-01 :3046) lists at
    /// <c>GET /api/masterdata/grunts</c>, read on 2026-10-21. The 5.4.0 OpenAPI document declares
    /// <c>grunt_type</c> as a free string and points at that route instead of carrying an enum.
    /// </summary>
    private static readonly string[] BedGruntTypes =
    [
        "arlo", "blanche", "bug", "candela", "cliff", "dark", "darkness", "decoy", "dragon", "electric",
        "explorer", "fairy", "fighting", "fire", "flying", "ghost", "giovanni", "grass", "ground", "gruntb",
        "ice", "mixed", "normal", "npc_0", "npc_1", "npc_10", "npc_11", "npc_12", "npc_13", "npc_14",
        "npc_15", "npc_16", "npc_17", "npc_18", "npc_19", "npc_2", "npc_20", "npc_3", "npc_4", "npc_5",
        "npc_6", "npc_7", "npc_8", "npc_9", "npc_blanche", "npc_candela", "npc_spark", "player_team_leader",
        "poison", "psychic", "rock", "sierra", "spark", "steel", "traveler", "unset", "water", "willow",
        "willowb",
    ];

    /// <summary>v2's catch-alls, which the grunt list does not carry (the <c>V2InvasionRule.grunt_type</c> description).</summary>
    private static readonly string[] CatchAlls = ["everything", "boss"];

    [Fact]
    public void EveryFannedOutNameIsOnePoracleNgAccepts()
    {
        var accepted = BedGruntTypes.Concat(CatchAlls).ToHashSet(StringComparer.Ordinal);

        Assert.All(InvasionGruntTypes.All, name => Assert.Contains(name, accepted));
    }

    [Fact]
    public void MatchesTheAngularDialogList()
    {
        var dialog = ReadDialogSource();

        // Each GRUNT_TYPES row looks like: { gruntType: 'fire', invasionId: 7, ... }
        var frontend = Regex.Matches(dialog, @"gruntType:\s*'([a-z-]+)'")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(frontend);

        var backend = InvasionGruntTypes.All.ToHashSet(StringComparer.Ordinal);

        var missingFromBackend = frontend.Except(backend).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var missingFromFrontend = backend.Except(frontend).OrderBy(x => x, StringComparer.Ordinal).ToList();

        Assert.True(
            missingFromBackend.Count == 0,
            $"The dialog offers grunt types the backend list lacks: {string.Join(", ", missingFromBackend)}. "
            + "Add them to InvasionGruntTypes or the 'All Invasions' quick pick will skip them.");

        Assert.True(
            missingFromFrontend.Count == 0,
            $"InvasionGruntTypes has entries the dialog does not offer: {string.Join(", ", missingFromFrontend)}. "
            + "Add them to GRUNT_TYPES or the dialog's 'Track all' will skip them.");
    }

    private static string ReadDialogSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Pgan.PoracleWebNet.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var path = Path.Combine(dir.FullName, DialogPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Could not find {path}");
        return File.ReadAllText(path);
    }
}
