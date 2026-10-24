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

    // A local that holds the caller's id: "var userId = this.UserId;" passed on later is the same bypass.
    private static readonly Regex Alias = new(@"\b(?:var|string)\s+(\w+)\s*=\s*this\.UserId\s*;", RegexOptions.Compiled);

    /// <summary>Every service call in <paramref name="text"/> that passes the caller's id, directly or through a local alias.</summary>
    private static IEnumerable<string> Offenders(string text)
    {
        foreach (Match m in Direct.Matches(text))
        {
            yield return m.Value;
        }

        foreach (var name in Alias.Matches(text).Select(m => m.Groups[1].Value).Distinct())
        {
            var viaAlias = new Regex(@"_\w+Service\s*\.\s*\w+Async\([^;]*?\b" + Regex.Escape(name) + @"\b", RegexOptions.Singleline);
            foreach (Match m in viaAlias.Matches(text))
            {
                yield return m.Value;
            }
        }
    }

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
            offenders.AddRange(Offenders(text).Select(m => $"{name}Controller: {m.ReplaceLineEndings(" ").Trim()}"));
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

    [Fact]
    public void TheScanFlagsTheCallersIdPassedThroughALocal()
    {
        Assert.NotEmpty(Offenders("var me = this.UserId;\nawait this._raidService.DeleteAsync(me, uid);"));
        Assert.NotEmpty(Offenders("string caller = this.UserId;\nawait this._gymService.UpdateDistanceByUidsAsync(\r\n  uids, caller, 5);"));
        Assert.Empty(Offenders("var me = this.UserId;\nawait this._router.FindAsync(this._raidService, me, uid);"));
        Assert.Empty(Offenders("var me = this.UserId;\nawait this._raidService.DeleteAsync(ownerId, uid);"));
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
