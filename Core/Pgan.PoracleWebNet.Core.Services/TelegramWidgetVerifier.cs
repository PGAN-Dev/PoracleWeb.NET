using System.Security.Cryptography;
using System.Text;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>Outcome of checking a Telegram login-widget payload. <c>Failure</c> is bad_hash, expired or missing_fields.</summary>
public sealed record TelegramWidgetResult(bool Valid, string? Failure, long UserId, string Name);

/// <summary>
/// The Telegram login-widget check, shared by sign-in (24-hour window) and account linking (10-minute window).
/// </summary>
public static class TelegramWidgetVerifier
{
    public static readonly TimeSpan SignInMaxAge = TimeSpan.FromHours(24);
    public static readonly TimeSpan LinkMaxAge = TimeSpan.FromMinutes(10);

    public static TelegramWidgetResult Verify(IReadOnlyDictionary<string, string> data, string botToken, TimeSpan maxAge, DateTimeOffset now)
    {
        if (!data.TryGetValue("id", out var idText) || !long.TryParse(idText, out var userId) ||
            !data.TryGetValue("hash", out var hash) ||
            !data.TryGetValue("auth_date", out var authDateText) || !long.TryParse(authDateText, out var authDateUnix))
        {
            return Fail("missing_fields");
        }

        // Expiry is judged before the hash, as sign-in always did.
        if (now - DateTimeOffset.FromUnixTimeSeconds(authDateUnix) > maxAge)
        {
            return Fail("expired");
        }

        var dataCheckString = string.Join("\n",
            data.Where(kvp => kvp.Key != "hash")
                .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
                .Select(kvp => $"{kvp.Key}={kvp.Value}"));
        var secretKey = SHA256.HashData(Encoding.UTF8.GetBytes(botToken));
        using var hmac = new HMACSHA256(secretKey);
        var computed = Encoding.UTF8.GetBytes(Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString))));

        if (!CryptographicOperations.FixedTimeEquals(computed, Encoding.UTF8.GetBytes(hash)))
        {
            return Fail("bad_hash");
        }

        var first = data.GetValueOrDefault("first_name", "");
        var last = data.GetValueOrDefault("last_name", "");
        var name = $"{first} {last}".Trim();
        if (name.Length == 0)
        {
            name = data.GetValueOrDefault("username", "");
        }

        return new TelegramWidgetResult(true, null, userId, name);
    }

    private static TelegramWidgetResult Fail(string failure) => new(false, failure, 0, "");
}
