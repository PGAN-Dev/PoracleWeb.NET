using System.Text.RegularExpressions;

namespace Pgan.PoracleWebNet.Tests.Architecture;

/// <summary>
/// A person with a linked Discord and Telegram account has two PoracleNG humans. Every person-level write
/// (location, places, areas, profiles, language, quiet periods, alerts on or off, summary schedules) has to
/// reach both, which only <c>IPairedHumanWriter</c> does. A controller method that calls one of these writes
/// without it would quietly update one side, so this fails the build when it appears.
/// </summary>
/// <remarks>
/// Service calls that write by themselves (<c>CopyAsync</c>, the overview duplicate and import) are listed
/// beside the proxy members: a controller wraps them as it does the proxy.
/// </remarks>
public sealed class PersonWritesUseWriterTests
{
    private static readonly string[] Files =
    [
        "Applications/Pgan.PoracleWebNet.Api/Controllers/LocationController.cs",
        "Applications/Pgan.PoracleWebNet.Api/Controllers/AreaController.cs",
        "Applications/Pgan.PoracleWebNet.Api/Controllers/UserGeofenceController.cs",
        "Applications/Pgan.PoracleWebNet.Api/Controllers/ProfileController.cs",
        "Applications/Pgan.PoracleWebNet.Api/Controllers/ProfileOverviewController.cs",
        "Applications/Pgan.PoracleWebNet.Api/Controllers/NotificationLanguageController.cs",
        "Applications/Pgan.PoracleWebNet.Api/Controllers/MuteController.cs",
        "Applications/Pgan.PoracleWebNet.Api/Controllers/SummaryScheduleController.cs",
        "Applications/Pgan.PoracleWebNet.Api/Controllers/AuthController.cs",
        "Core/Pgan.PoracleWebNet.Core.Services/UserGeofenceService.cs",
    ];

    private static readonly Regex Writes = new(
        @"_\w+\.(SetLocationAsync|AddPlaceAsync|UpdatePlaceAsync|DeletePlaceAsync|SetProfileGeographyAsync|AddProfileAsync|UpdateProfileAsync|DeleteProfileAsync|SwitchProfileAsync|CopyProfileAsync|SetLanguageAsync|StartAsync|StopAsync|SaveSelectionAsync|AddToActiveProfileAsync|RemoveFromActiveProfileAsync|SetScheduleAsync|DeleteScheduleAsync|CopyAsync|DuplicateProfileAsync|ImportAlarmsAsync)\(" +
        @"|_muteProxy\.(CreateAsync|DeleteAsync|DeleteAllAsync)\(",
        RegexOptions.Compiled);

    // Controller and service members start at four spaces of indent; a method's body is indented deeper.
    // Attributes and doc comments above a declaration fall into the previous chunk, which holds no calls.
    private static readonly Regex DeclarationStart = new(@"^    (public|private|internal|protected) ", RegexOptions.Compiled);

    [Fact]
    public void EveryPersonWriteGoesThroughThePairedWriter()
    {
        var root = FindSolutionRoot();
        var offenders = new List<string>();

        foreach (var relative in Files)
        {
            var lines = File.ReadAllLines(Path.Combine(root, relative));
            var chunk = new List<string>();
            var header = string.Empty;

            void Flush()
            {
                var body = string.Join('\n', chunk);
                if (Writes.IsMatch(body) && !body.Contains("_pairWriter.WriteAsync", StringComparison.Ordinal))
                {
                    offenders.Add($"{relative}: {header.Trim()}");
                }

                chunk.Clear();
            }

            foreach (var line in lines)
            {
                if (DeclarationStart.IsMatch(line))
                {
                    Flush();
                    header = line;
                }

                chunk.Add(line);
            }

            Flush();
        }

        Assert.True(offenders.Count == 0,
            "Person-level writes must go through _pairWriter.WriteAsync so a linked account's other human gets them too:\n"
            + string.Join('\n', offenders));
    }

    private static string FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Pgan.PoracleWebNet.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }
}
