using System.Text.RegularExpressions;

namespace Pgan.PoracleWebNet.Tests.Architecture;

/// <summary>
/// PoracleWeb.NET no longer connects to Poracle's database: every read and write goes through PoracleNG's
/// API. This fails the build if production code names the context, the old settings table, the connection
/// string or the <c>DB_*</c> variables that composed it, so the dependency cannot creep back unnoticed.
/// </summary>
public sealed class NoPoracleDatabaseTests
{
    private static readonly string[] ScannedRoots =
    [
        "Core",
        "Data",
        "Applications/Pgan.PoracleWebNet.Api",
    ];

    // Matches the exact forms, not prose: the startup log line names "DB_*" without any of these.
    private static readonly Regex[] Forbidden =
    [
        new(@"PoracleContext", RegexOptions.Compiled),
        new(@"PwebSetting", RegexOptions.Compiled),
        new("\"PoracleDb\"", RegexOptions.Compiled),
        new(@"ConnectionStrings:PoracleDb", RegexOptions.Compiled),
        new(@"ConnectionStrings__PoracleDb", RegexOptions.Compiled),
        new(@"""PoracleDb""\s*:", RegexOptions.Compiled),
        new("(MapEnvVar|ComposeConnectionString)\\s*\\([^)]*\"DB_HOST\"", RegexOptions.Compiled),
        new("\"pweb_settings\"", RegexOptions.Compiled),
        new(@"GetEnvironmentVariable\s*\(\s*""DB_(HOST|PORT|NAME|USER|PASSWORD)""", RegexOptions.Compiled),
        new(@"\[Table\s*\(\s*""(humans|profiles)""", RegexOptions.Compiled),
    ];

    // The single place allowed to name the retired connection string: the line that detects a leftover one.
    private const string AllowMarker = "// legacy-db-setting-detection";
    private const string AllowedFile = "Applications/Pgan.PoracleWebNet.Api/Program.cs";

    [Fact]
    public void NoProductionCodeReferencesPoraclesDatabase()
    {
        var root = FindSolutionRoot();
        var offenders = new List<string>();

        foreach (var rootName in ScannedRoots)
        {
            var files = FilesUnder(root, rootName, "*.cs").ToList();
            Assert.True(files.Count > 0, $"Scanned no .cs files under {rootName}, so this test would pass by scanning nothing.");

            foreach (var file in files)
            {
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (Violates(Path.GetRelativePath(root, file), lines[i]))
                    {
                        offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}");
                    }
                }
            }
        }

        // Configuration too: a "PoracleDb" key in appsettings is the same dependency as the code reading it.
        var jsonFiles = FilesUnder(root, "Applications/Pgan.PoracleWebNet.Api", "*.json").ToList();
        Assert.True(jsonFiles.Count > 0, "Scanned no .json files under the API project, so the configuration check would pass by scanning nothing.");
        foreach (var file in jsonFiles)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (Violates(Path.GetRelativePath(root, file), lines[i]))
                {
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Poracle's database is referenced again: " + string.Join(", ", offenders.Order(StringComparer.Ordinal))
            + ". PoracleWeb.NET reaches Poracle only through PoracleNG's API.");
    }

    [Theory]
    [InlineData("services.AddDbContext<PoracleContext>(o => { });")]
    [InlineData("public DbSet<PwebSettingEntity> PwebSettings { get; }")]
    [InlineData("configuration.GetConnectionString(\"PoracleDb\")")]
    [InlineData("builder.UseSetting(\"ConnectionStrings:PoracleDb\", x);")]
    [InlineData("Environment.SetEnvironmentVariable(\"ConnectionStrings__PoracleDb\", x);")]
    [InlineData("MapEnvVar(\"DB_HOST\", \"Whatever\");")]
    [InlineData("ComposeConnectionString(\"X\", \"DB_HOST\", \"DB_PORT\");")]
    [InlineData("var t = \"pweb_settings\";")]
    [InlineData("Environment.GetEnvironmentVariable(\"DB_HOST\")")]
    [InlineData("Environment.GetEnvironmentVariable(\"DB_PASSWORD\")")]
    [InlineData("[Table(\"humans\")]")]
    [InlineData("[Table(\"profiles\")]")]
    public void TheMatcherFindsEachForbiddenForm(string planted) =>
        Assert.True(Matches(planted), $"The matcher missed: {planted}");

    [Theory]
    [InlineData("    \"PoracleDb\": \"\",")]
    [InlineData("  \"ConnectionStrings:PoracleDb\": \"x\"")]
    public void TheMatcherFindsAConfigurationKey(string line) =>
        Assert.True(Matches(line), $"The matcher missed: {line}");

    [Fact]
    public void TheAllowlistCoversOnlyTheMarkedLineInProgramCs()
    {
        var marked = "private const string ConnectionName = \"PoracleDb\"; " + AllowMarker;

        Assert.False(Violates(AllowedFile, marked));
        Assert.False(Violates(AllowedFile.Replace('/', Path.DirectorySeparatorChar), marked));
        Assert.True(Violates(AllowedFile, "private const string ConnectionName = \"PoracleDb\";"));
        Assert.True(Violates("Core/Pgan.PoracleWebNet.Core.Services/Other.cs", marked));
        Assert.True(Violates("Applications/Pgan.PoracleWebNet.Api/appsettings.json", "\"PoracleDb\": \"\", " + AllowMarker));
    }

    [Theory]
    [InlineData("ComposeConnectionString(\"PoracleWebDb\", \"WEB_DB_HOST\", \"WEB_DB_PORT\");")]
    [InlineData("var context = typeof(PoracleWebContext);")]
    [InlineData("Environment.GetEnvironmentVariable(\"WEB_DB_HOST\")")]
    [InlineData("[Table(\"user_geofences\")]")]
    [InlineData("DB_* settings are no longer used by PoracleWeb.NET and can be removed.")]
    public void TheMatcherLeavesTheWebDatabaseAlone(string line) =>
        Assert.False(Matches(line), $"The matcher flagged: {line}");

    private static bool Matches(string line) => Forbidden.Any(r => r.IsMatch(line));

    private static bool Violates(string relativePath, string line)
    {
        var normalised = relativePath.Replace(Path.DirectorySeparatorChar, '/');
        var allowed = string.Equals(normalised, AllowedFile, StringComparison.Ordinal)
            && line.Contains(AllowMarker, StringComparison.Ordinal);
        return !allowed && Matches(line);
    }

    private static IEnumerable<string> FilesUnder(string root, string rootName, string pattern)
    {
        var dir = Path.Combine(root, rootName.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(dir))
        {
            return [];
        }

        return Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(root, f)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(s => s is "bin" or "obj"));
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
