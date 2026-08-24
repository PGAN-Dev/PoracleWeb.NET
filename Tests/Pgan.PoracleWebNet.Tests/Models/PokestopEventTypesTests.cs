using System.Text.RegularExpressions;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Models;

/// <summary>
/// The event table is a mirror of PoracleNG's <c>pokestopEvent</c> block, and it has a twin in the
/// SPA. Two copies of the same table drift.
/// </summary>
public class PokestopEventTypesTests
{
    [Theory]
    [InlineData(PokestopEventTypes.GoldStop, "gold-stop")]
    [InlineData(PokestopEventTypes.Kecleon, "kecleon")]
    [InlineData(PokestopEventTypes.Showcase, "showcase")]
    public void EachEventNamesTheGruntTypeItIsStoredAs(int displayType, string expected)
    {
        // The names are what PoracleNG writes into grunt_type: lower(pokestopEvent[id].name).
        Assert.Equal(expected, PokestopEventTypes.NameFor(displayType));
        Assert.Equal(displayType, PokestopEventTypes.DisplayTypeFor(expected));
    }

    [Fact]
    public void EventNamesAreRecognisedRegardlessOfCase()
    {
        // PoracleNG lowercases both sides of this comparison; a row written by another client may not.
        Assert.True(PokestopEventTypes.IsEventName("Showcase"));
        Assert.True(PokestopEventTypes.IsEventName("GOLD-STOP"));
    }

    /// <summary>
    /// The partition's whole job. Every grunt_type an invasion rule can hold must read as not-an-event,
    /// or real invasion alarms vanish from their list.
    /// </summary>
    [Theory]
    [InlineData("water")]
    [InlineData("everything")]
    [InlineData("boss")]
    [InlineData("blanche")]
    [InlineData("npc 3")]
    [InlineData("player team leader")]
    [InlineData("")]
    [InlineData(null)]
    public void InvasionGruntTypesAreNotEvents(string? gruntType) =>
        Assert.False(PokestopEventTypes.IsEventName(gruntType));

    /// <summary>
    /// Holds the C# table and the SPA's <c>POKESTOP_EVENTS</c> table in step. They are read by
    /// different halves of the same feature — the backend partitions on the name, the frontend labels
    /// and colours by the id — so a one-sided edit produces a rule nobody can see or name.
    /// </summary>
    [Fact]
    public void TheFrontendTwinAgreesEntryForEntry()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "Applications",
            "Pgan.PoracleWebNet.App",
            "ClientApp",
            "src",
            "app",
            "shared",
            "utils",
            "pokestop-events.ts"));

        // Read the two fields that matter as parallel sequences over the array literal rather than
        // per-object, so the check does not depend on which order the formatter last put them in.
        var array = Regex.Match(source, @"POKESTOP_EVENTS[^=]*=\s*\[(.*?)\n\];", RegexOptions.Singleline);
        Assert.True(array.Success, "Could not find the POKESTOP_EVENTS array in the frontend twin.");

        var names = Regex.Matches(array.Groups[1].Value, @"name:\s*'([a-z-]+)'").Select(m => m.Groups[1].Value).ToList();
        var ids = Regex.Matches(array.Groups[1].Value, @"displayType:\s*(\d+)")
            .Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        Assert.Equal(names.Count, ids.Count);
        var twin = ids.Zip(names).ToDictionary(x => x.First, x => x.Second);

        Assert.Equal(PokestopEventTypes.ById.OrderBy(x => x.Key), twin.OrderBy(x => x.Key));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
