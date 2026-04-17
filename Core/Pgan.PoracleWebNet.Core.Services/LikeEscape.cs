namespace Pgan.PoracleWebNet.Core.Services;

public static class LikeEscape
{
    public static string Escape(string input) => input
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);
}
