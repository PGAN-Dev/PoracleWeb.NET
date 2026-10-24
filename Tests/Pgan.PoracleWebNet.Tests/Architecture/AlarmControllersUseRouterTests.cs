using System.Text.RegularExpressions;

namespace Pgan.PoracleWebNet.Tests.Architecture;

/// <summary>
/// The eleven alarm controllers reach their service only through <c>LinkedAlarmRouter</c>, so a linked person's
/// second human is never skipped by one action that still passes the caller's own id straight through.
/// </summary>
public sealed class AlarmControllersUseRouterTests
{
    private static readonly string[] Controllers =
    [
        "Monster", "Raid", "Egg", "Quest", "Invasion", "Lure", "Nest", "Gym", "MaxBattle", "FortChange", "PokestopEvent",
    ];

    // A service call whose arguments (up to the end of the statement) name this.UserId.
    private static readonly Regex Direct = new(@"_\w+Service\s*\.\s*\w+Async\([^;]*?this\.UserId", RegexOptions.Compiled | RegexOptions.Singleline);

    [Fact]
    public void NoAlarmControllerCallsItsServiceWithTheCallersId()
    {
        var root = FindSolutionRoot();
        var offenders = new List<string>();
        foreach (var name in Controllers)
        {
            var path = Path.Combine(root, "Applications", "Pgan.PoracleWebNet.Api", "Controllers", name + "Controller.cs");
            Assert.True(File.Exists(path), $"{path} is missing, so this test would pass by scanning nothing.");
            var text = File.ReadAllText(path);
            offenders.AddRange(Direct.Matches(text).Select(m => $"{name}Controller: {m.Value.ReplaceLineEndings(" ").Trim()}"));
            Assert.Contains("LinkedAlarmRouter", text);
        }

        Assert.True(offenders.Count == 0, "Route these through LinkedAlarmRouter:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void TheScanFlagsADirectCallAndAcceptsARouterCall()
    {
        Assert.Matches(Direct, "await this._raidService.GetByUidAsync(this.UserId, uid);");
        Assert.Matches(Direct, "await this._x.UpdateDistanceByUidsAsync(\r\n  request.Uids, this.UserId, 5);".Replace("_x", "_xService"));
        Assert.DoesNotMatch(Direct, "await this._router.FindAsync(this._raidService, this.UserId, uid);");
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
