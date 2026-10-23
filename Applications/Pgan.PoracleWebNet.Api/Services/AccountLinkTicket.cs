using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Pgan.PoracleWebNet.Api.Services;

/// <summary>
/// The <c>oauth_link</c> cookie: who started a Discord link, sealed so the callback can trust it
/// without a session token. A cookie that is missing, tampered with or past its expiry opens to null.
/// </summary>
internal static class AccountLinkTicket
{
    public const string CookieName = "oauth_link";
    public const string Purpose = "AccountLink.v1";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private sealed record Payload(string HumanId, DateTimeOffset Expires);

    public static string Seal(IDataProtectionProvider provider, string humanId, DateTimeOffset now) =>
        provider.CreateProtector(Purpose).Protect(JsonSerializer.Serialize(new Payload(humanId, now + Lifetime)));

    public static string? Open(IDataProtectionProvider provider, string? sealedValue, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(sealedValue))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(provider.CreateProtector(Purpose).Unprotect(sealedValue));
            return payload is { HumanId.Length: > 0 } && payload.Expires > now ? payload.HumanId : null;
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException)
        {
            return null;
        }
    }
}
