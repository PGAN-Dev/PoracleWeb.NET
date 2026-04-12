namespace Pgan.PoracleWebNet.Core.Models.Pvp;

public enum PvpLeague
{
    Little = 1,
    Great = 2,
    Ultra = 3,
    Master = 4,
}

public static class PvpLeagueExtensions
{
    public static int CpCap(this PvpLeague league) => league switch
    {
        PvpLeague.Little => 500,
        PvpLeague.Great => 1500,
        PvpLeague.Ultra => 2500,
        PvpLeague.Master => int.MaxValue,
        _ => throw new ArgumentOutOfRangeException(nameof(league), league, null),
    };
}
