using System.Text.RegularExpressions;

namespace Pgan.PoracleWebNet.Tests.Repositories;

/// <summary>
/// <c>ExecuteDeleteAsync</c> is unusable against this deployment. MySql.EntityFrameworkCore emits the
/// aliased single-table form — <c>DELETE FROM `t` AS `x` WHERE …</c> — and MariaDB answers 1064; the
/// multi-table <c>DELETE x FROM t AS x</c> is required once an alias is present. Verified against
/// MariaDB 10.8.2.
///
/// Nothing catches this at build time and nothing catches it in the test suite either, because the
/// repository tests run on SQLite, whose provider does not emit the alias. It reaches production green
/// and fails on every call. That is how the OIDC session cleanup shipped never having run once (#707),
/// and how <c>QuickPickAppliedStateRepository</c> acquired its load-and-remove workaround before it.
///
/// Use raw SQL with unquoted identifiers, or load and <c>RemoveRange</c> when the row count is small.
/// <c>ExecuteUpdateAsync</c> is fine — MariaDB accepts <c>UPDATE t AS x SET x.c = …</c>.
/// </summary>
public sealed class NoAliasedDeleteTests
{
    private static readonly string[] ScannedProjects =
    [
        "Core",
        "Data",
        "Applications/Pgan.PoracleWebNet.Api",
    ];

    // Matches the call, not prose: the surrounding comments and doc-comments name the method freely.
    private static readonly Regex CallSite = new(@"\.ExecuteDeleteAsync\s*\(", RegexOptions.Compiled);

    [Fact]
    public void NoProductionCodeCallsExecuteDeleteAsync()
    {
        var root = FindSolutionRoot();

        var offenders = ScannedProjects
            .Select(p => Path.Combine(root, p.Replace('/', Path.DirectorySeparatorChar)))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            .Where(f => !IsBuildOutput(f, root))
            .SelectMany(f => File.ReadLines(f)
                .Select((line, i) => (line, number: i + 1))
                .Where(x => CallSite.IsMatch(x.line))
                .Select(x => $"{Path.GetRelativePath(root, f)}:{x.number}"))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "ExecuteDeleteAsync is back: " + string.Join(", ", offenders)
            + ". The provider emits DELETE FROM `t` AS `x`, which MariaDB rejects with a 1064 at runtime "
            + "while the SQLite-backed tests stay green. Use raw SQL with unquoted identifiers, or load "
            + "the rows and RemoveRange them. See #707.");
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
