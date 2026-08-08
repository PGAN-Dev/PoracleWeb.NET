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
