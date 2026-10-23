using System.Globalization;
using System.Text.RegularExpressions;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Models;

/// <summary>
/// The SPA's <c>MAX_RADIUS_METRES</c> is the bulk distance dialog's ceiling and is documented as the API's
/// own cap. If <see cref="AlarmDistance.MaxMetres"/> moves and the twin doesn't, the dialog either refuses
/// a radius the API takes or offers one the API answers 400 to.
/// </summary>
public class AlarmDistanceTwinTests
{
    [Fact]
    public void TheSpaCapIsTheApiCap()
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
            "alarm-scope.ts"));

        var match = Regex.Match(source, @"export const MAX_RADIUS_METRES\s*=\s*([\d_]+)\s*;");
        Assert.True(match.Success, "Could not find MAX_RADIUS_METRES in alarm-scope.ts.");

        var twin = int.Parse(match.Groups[1].Value.Replace("_", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture);
        Assert.Equal(AlarmDistance.MaxMetres, twin);
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
