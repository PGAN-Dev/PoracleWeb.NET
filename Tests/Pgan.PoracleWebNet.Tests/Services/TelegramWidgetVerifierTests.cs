using System.Security.Cryptography;
using System.Text;
using Pgan.PoracleWebNet.Core.Services;
using Xunit;

namespace Pgan.PoracleWebNet.Tests.Services;

public class TelegramWidgetVerifierTests
{
    private const string Token = "123456:test-token";
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A payload signed with <see cref="Token"/>. <paramref name="shape"/> runs before signing, <paramref name="tamper"/> after.</summary>
    private static Dictionary<string, string> Signed(
        TimeSpan age, Action<Dictionary<string, string>>? tamper = null, bool omitHash = false, Action<Dictionary<string, string>>? shape = null)
    {
        var data = new Dictionary<string, string>
        {
            ["id"] = "200000001",
            ["first_name"] = "Riverside",
            ["last_name"] = "Rider",
            ["username"] = "riverside_rider",
            ["auth_date"] = (Now - age).ToUnixTimeSeconds().ToString(),
        };
        shape?.Invoke(data);
        var check = string.Join("\n", data.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key}={k.Value}"));
        using var hmac = new HMACSHA256(SHA256.HashData(Encoding.UTF8.GetBytes(Token)));
        if (!omitHash)
        {
            data["hash"] = Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(check)));
        }
        tamper?.Invoke(data);
        return data;
    }

    [Fact]
    public void ValidPayloadPassesWithIdAndName()
    {
        var r = TelegramWidgetVerifier.Verify(Signed(TimeSpan.FromMinutes(1)), Token, TelegramWidgetVerifier.LinkMaxAge, Now);
        Assert.True(r.Valid);
        Assert.Null(r.Failure);
        Assert.Equal(200000001, r.UserId);
        Assert.Equal("Riverside Rider", r.Name);
    }

    [Fact]
    public void NameFallsBackToUsername()
    {
        var data = Signed(TimeSpan.Zero, shape: d =>
        {
            d.Remove("first_name");
            d.Remove("last_name");
        });
        var r = TelegramWidgetVerifier.Verify(data, Token, TelegramWidgetVerifier.SignInMaxAge, Now);
        Assert.True(r.Valid);
        Assert.Equal("riverside_rider", r.Name);
    }

    [Fact]
    public void TamperedFieldFailsBadHash()
    {
        var r = TelegramWidgetVerifier.Verify(Signed(TimeSpan.Zero, d => d["id"] = "200000002"), Token, TelegramWidgetVerifier.SignInMaxAge, Now);
        Assert.False(r.Valid);
        Assert.Equal("bad_hash", r.Failure);
    }

    [Fact]
    public void ElevenMinutesOldIsExpiredForLinkButFineForSignIn()
    {
        var data = Signed(TimeSpan.FromMinutes(11));
        Assert.Equal("expired", TelegramWidgetVerifier.Verify(data, Token, TelegramWidgetVerifier.LinkMaxAge, Now).Failure);
        Assert.True(TelegramWidgetVerifier.Verify(data, Token, TelegramWidgetVerifier.SignInMaxAge, Now).Valid);
    }

    [Fact]
    public void MissingHashFailsMissingFields()
    {
        var r = TelegramWidgetVerifier.Verify(Signed(TimeSpan.Zero, omitHash: true), Token, TelegramWidgetVerifier.SignInMaxAge, Now);
        Assert.Equal("missing_fields", r.Failure);
    }

    [Fact]
    public void NonNumericIdFailsBadId()
    {
        var r = TelegramWidgetVerifier.Verify(Signed(TimeSpan.Zero, shape: d => d["id"] = "abc"), Token, TelegramWidgetVerifier.SignInMaxAge, Now);
        Assert.False(r.Valid);
        Assert.Equal("bad_id", r.Failure);
    }

    [Fact]
    public void NonNumericAuthDateFailsBadAuthDate()
    {
        var r = TelegramWidgetVerifier.Verify(Signed(TimeSpan.Zero, shape: d => d["auth_date"] = "yesterday"), Token, TelegramWidgetVerifier.SignInMaxAge, Now);
        Assert.False(r.Valid);
        Assert.Equal("bad_auth_date", r.Failure);
    }

    [Fact]
    public void AuthDateWellInTheFutureFailsBadAuthDate()
    {
        var r = TelegramWidgetVerifier.Verify(Signed(-TimeSpan.FromHours(1)), Token, TelegramWidgetVerifier.LinkMaxAge, Now);
        Assert.False(r.Valid);
        Assert.Equal("bad_auth_date", r.Failure);
    }

    [Fact]
    public void AuthDateAMinuteAheadIsClockDriftAndPasses()
    {
        var r = TelegramWidgetVerifier.Verify(Signed(-TimeSpan.FromMinutes(1)), Token, TelegramWidgetVerifier.LinkMaxAge, Now);
        Assert.True(r.Valid);
    }
}
