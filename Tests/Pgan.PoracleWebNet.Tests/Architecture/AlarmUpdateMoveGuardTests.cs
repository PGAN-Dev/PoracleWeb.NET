namespace Pgan.PoracleWebNet.Tests.Architecture;

/// <summary>
/// Each of the eleven alarm controllers that can move an alarm from <c>PUT /{uid}</c> refuses the move while an
/// account is being inspected, before anything is merged or written.
/// </summary>
public sealed class AlarmUpdateMoveGuardTests
{
    private static readonly string[] Controllers =
    [
        "Monster", "Raid", "Egg", "Quest", "Invasion", "Lure", "Nest", "Gym", "MaxBattle", "FortChange", "PokestopEvent",
    ];

    [Fact]
    public void EveryControllerRefusesAMoveWhileImpersonatingBeforeItMoves()
    {
        var root = FindSolutionRoot();
        foreach (var name in Controllers)
        {
            var path = Path.Combine(root, "Applications", "Pgan.PoracleWebNet.Api", "Controllers", name + "Controller.cs");
            Assert.True(File.Exists(path), $"{path} is missing, so this test would pass by scanning nothing.");
            var text = File.ReadAllText(path);

            var target = text.IndexOf("MoveTargetAsync", StringComparison.Ordinal);
            var guard = text.IndexOf("RefuseMoveWhileImpersonating", StringComparison.Ordinal);
            var move = text.IndexOf("this._router.MoveAsync", StringComparison.Ordinal);
            Assert.True(target >= 0 && move >= 0, $"{name}Controller no longer moves from Update; update this test.");
            Assert.True(guard > target && guard < move, $"{name}Controller must call RefuseMoveWhileImpersonating between MoveTargetAsync and MoveAsync.");
        }
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
