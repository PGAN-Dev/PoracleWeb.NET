using System.Text.RegularExpressions;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// PoracleWeb.NET speaks only PoracleNG's <c>/api/v2</c> tracking surface. Every read, create, edit and delete
/// moved there first, and bulk distance and the clean toggle were the last v1 callers: their v1 upsert of a
/// rule created through v2 without a template saw two updatable differences (v1 fills <c>""</c> with the
/// server default name) and inserted a second rule instead of updating the first. Both now use v2's
/// <c>PATCH</c>.
/// </summary>
/// <remarks>
/// A v1 tracking route in a string literal is how one would come back, so this fails the build on any. Prose
/// may still name the v1 routes; only code is scanned.
/// </remarks>
public sealed partial class NoV1TrackingCallsTests
{
    private static readonly string[] ScannedProjects =
    [
        "Core",
        "Applications/Pgan.PoracleWebNet.Api",
    ];

    private static readonly Regex V1Route = V1RouteLiteral();

    // A string literal (plain or interpolated) that reaches a v1 tracking route.
    [GeneratedRegex("\"[^\"\\n]*/api/tracking[/\"?]")]
    private static partial Regex V1RouteLiteral();

    [Fact]
    public void NoProductionCodeCallsTheV1TrackingRoutes()
    {
        var root = FindSolutionRoot();

        var files = ScannedProjects
            .Select(p => Path.Combine(root, p.Replace('/', Path.DirectorySeparatorChar)))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            .Where(f => !IsBuildOutput(f, root))
            .ToList();

        // A moved or renamed project would otherwise leave nothing to scan, and an empty scan passes.
        Assert.Contains(files, f => f.EndsWith("PoracleTrackingProxy.Writes.cs", StringComparison.Ordinal));

        var offenders = files
            .SelectMany(f => File.ReadLines(f)
                .Select((line, i) => (line, number: i + 1))
                .Where(x => !x.line.TrimStart().StartsWith("//", StringComparison.Ordinal) && V1Route.IsMatch(x.line))
                .Select(x => $"{Path.GetRelativePath(root, f)}:{x.number}"))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "A v1 tracking route is back: " + string.Join(", ", offenders)
            + ". PoracleWeb.NET writes and reads tracking through /api/v2 only; v1's upsert re-fills an empty "
            + "template with the server default and inserts a second rule where it should update one.");
    }

    [Fact]
    public void TheScanFindsAV1RouteInALiteralButNotInProse()
    {
        // The guard is only worth having if it can go red.
        Assert.Matches(V1Route, "        var url = $\"{this._apiAddress}/api/tracking/{type}/{Encode(userId)}?silent=true\";");
        Assert.Matches(V1Route, "        const string Route = \"/api/tracking\";");
        Assert.DoesNotMatch(V1Route, "        var url = $\"{this._apiAddress}/api/v2/humans/{id}/tracking/{type}?silent=true\";");
        Assert.DoesNotMatch(V1Route, "    /// The keys v1's <c>/api/tracking/all</c> answered.");
    }

    private static bool IsBuildOutput(string file, string root)
    {
        var relative = Path.GetRelativePath(root, file);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(s => s is "bin" or "obj");
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
