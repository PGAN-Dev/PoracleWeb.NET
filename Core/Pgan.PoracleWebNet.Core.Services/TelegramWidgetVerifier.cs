using System.Security.Cryptography;
using System.Text;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Outcome of checking a Telegram login-widget payload. <c>Failure</c> is missing_fields (no id or hash), bad_id (an id
/// that is not a number), bad_auth_date (missing, not a number, or in the future), expired or bad_hash.
/// </summary>
public sealed record TelegramWidgetResult(bool Valid, string? Failure, long UserId, string Name);

/// <summary>
/// The Telegram login-widget check, shared by sign-in (24-hour window) and account linking (10-minute window).
/// </summary>
public static class TelegramWidgetVerifier
{
    public static readonly TimeSpan SignInMaxAge = TimeSpan.FromHours(24);
    public static readonly TimeSpan LinkMaxAge = TimeSpan.FromMinutes(10);

    /// <summary>How far ahead of this server's clock an auth_date may be before it is refused, for clock drift.</summary>
    public static readonly TimeSpan FutureSkew = TimeSpan.FromMinutes(5);

    public static TelegramWidgetResult Verify(IReadOnlyDictionary<string, string> data, string botToken, TimeSpan maxAge, DateTimeOffset now)
    {
        if (!data.TryGetValue("id", out var idText) || !data.TryGetValue("hash", out var hash))
        {
            return Fail("missing_fields");
        }

        if (!long.TryParse(idText, out var userId))
        {
            return Fail("bad_id");
        }

        if (!data.TryGetValue("auth_date", out var authDateText) || !long.TryParse(authDateText, out var authDateUnix)
            || authDateUnix < 0 || authDateUnix > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return Fail("bad_auth_date");
        }

        // Expiry is judged before the hash, as sign-in always did. A date ahead of the clock by more than drift
        // explains would otherwise stay valid for as long again as it is early.
        var age = now - DateTimeOffset.FromUnixTimeSeconds(authDateUnix);
        if (age < -FutureSkew)
        {
            return Fail("bad_auth_date");
        }

        if (age > maxAge)
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
