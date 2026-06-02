namespace Pgan.PoracleWebNet.Core.Services;

public static class LikeEscape
{
    // Use `|` instead of the more conventional `\` because MariaDB's default
    // mode treats `\` as a string-literal escape too — a user-supplied `\` in
    // the search term left an unbalanced quote and broke gym search (#260).
    public const string EscapeChar = "|";

    public static string Escape(string input) => input
        .Replace("|", "||", StringComparison.Ordinal)
        .Replace("%", "|%", StringComparison.Ordinal)
        .Replace("_", "|_", StringComparison.Ordinal);
}
