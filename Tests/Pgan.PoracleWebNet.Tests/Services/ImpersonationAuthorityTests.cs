using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Api.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// An impersonation token used to be authorised once, when it was minted, and never again. Revoking a
/// delegate's grant stopped them minting a new one while the one they held kept full read and write
/// access for the rest of its 24 hours -- and a profile switch re-issued it. The session is now checked
/// against the impersonator's live roles on every request.
/// </summary>
public class ImpersonationAuthorityTests
{
    private const string Delegate = "delegate-1";
    private const string Webhook = "https://discordapp.com/api/webhooks/1/token";

    private readonly Mock<IUserRoleResolver> _resolver = new();

    private ImpersonationAuthority CreateSut() => new(this._resolver.Object, NullLogger<ImpersonationAuthority>.Instance);

    private static ClaimsPrincipal Session(string userId, string? impersonatedBy)
    {
        var claims = new List<Claim> { new("userId", userId), new("profileNo", "1"), new("isAdmin", "false") };
        if (impersonatedBy is not null)
        {
            claims.Add(new Claim("impersonatedBy", impersonatedBy));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private void Roles(string userId, UserRoles roles) =>
        this._resolver.Setup(r => r.ResolveAsync(userId)).ReturnsAsync(roles);

    [Fact]
    public async Task AnOrdinarySessionIsNeverAskedAbout()
    {
        Assert.True(await this.CreateSut().StillHoldsAsync(Session("u1", impersonatedBy: null)));

        this._resolver.Verify(r => r.ResolveAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ADelegateWhoStillHoldsTheGrantKeepsTheSession()
    {
        this.Roles(Delegate, new UserRoles(false, [Webhook]));

        Assert.True(await this.CreateSut().StillHoldsAsync(Session(Webhook, Delegate)));
    }

    [Fact]
    public async Task TheGrantIsMatchedWithoutRegardToCase()
    {
        // ImpersonateById compares case-insensitively; this check must agree with the one that minted it.
        this.Roles(Delegate, new UserRoles(false, [Webhook.ToUpperInvariant()]));

        Assert.True(await this.CreateSut().StillHoldsAsync(Session(Webhook, Delegate)));
    }

    [Fact]
    public async Task ARevokedDelegateLosesTheSession()
    {
        this.Roles(Delegate, new UserRoles(false, null));

        Assert.False(await this.CreateSut().StillHoldsAsync(Session(Webhook, Delegate)));
    }

    [Fact]
    public async Task ADelegateOfADifferentWebhookLosesTheSession()
    {
        this.Roles(Delegate, new UserRoles(false, ["https://discordapp.com/api/webhooks/2/other"]));

        Assert.False(await this.CreateSut().StillHoldsAsync(Session(Webhook, Delegate)));
    }

    [Fact]
    public async Task AnAdminImpersonatingAnyoneKeepsTheSession()
    {
        this.Roles("admin-1", new UserRoles(true, null));

        Assert.True(await this.CreateSut().StillHoldsAsync(Session("some-user", "admin-1")));
    }

    [Fact]
    public async Task ADemotedAdminLosesTheSession()
    {
        this.Roles("admin-1", new UserRoles(false, null));

        Assert.False(await this.CreateSut().StillHoldsAsync(Session("some-user", "admin-1")));
    }

    /// <summary>
    /// Deliberately fails open. The resolver reports an answer it could not complete as unresolved, and
    /// treating that as "no authority" would end every impersonation session on the site whenever
    /// PoracleNG or the poracle_web database blinked -- the #656 shape. Revocation lands on the first
    /// request after the sources are readable again.
    /// </summary>
    [Fact]
    public async Task AnAnswerThatCouldNotBeResolvedDoesNotEndTheSession()
    {
        this.Roles(Delegate, new UserRoles(false, null, Resolved: false));

        Assert.True(await this.CreateSut().StillHoldsAsync(Session(Webhook, Delegate)));
    }

    [Fact]
    public async Task ASessionNamingNoAccountIsRefused()
    {
        this.Roles("admin-1", new UserRoles(true, null));
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("impersonatedBy", "admin-1")], "TestAuth"));

        Assert.False(await this.CreateSut().StillHoldsAsync(principal));
    }
}
