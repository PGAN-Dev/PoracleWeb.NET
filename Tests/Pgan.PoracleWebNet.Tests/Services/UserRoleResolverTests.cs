using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Services;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The resolver must distinguish "resolved: not an admin" from "could not resolve". Treating the second
/// as the first meant a PoracleNG blip during a profile switch stripped admin for the rest of the
/// session, and cached that answer for a minute. See #656.
/// </summary>
public class UserRoleResolverTests
{
    private readonly Mock<IPoracleApiProxy> _poracleApiProxy = new();
    private readonly Mock<IWebhookDelegateService> _webhookDelegateService = new();

    private UserRoleResolver CreateSut(string adminIds = "") => new(
        this._poracleApiProxy.Object,
        this._webhookDelegateService.Object,
        Options.Create(new PoracleSettings { AdminIds = adminIds }),
        new MemoryCache(new MemoryCacheOptions()),
        NullLogger<UserRoleResolver>.Instance);

    [Fact]
    public async Task AnUnreachablePoracleIsReportedAsUnresolvedRatherThanAsNotAnAdmin()
    {
        this._poracleApiProxy.Setup(p => p.GetConfigAsync()).ThrowsAsync(new HttpRequestException("down"));
        this._poracleApiProxy.Setup(p => p.GetAdminRolesAsync(It.IsAny<string>())).ThrowsAsync(new HttpRequestException("down"));
        this._webhookDelegateService.Setup(s => s.GetManagedWebhookIdsAsync(It.IsAny<string>())).ReturnsAsync([]);

        var roles = await this.CreateSut().ResolveAsync("u1");

        Assert.False(roles.Resolved);
    }

    [Fact]
    public async Task ADegradedAnswerIsNotCached()
    {
        // Caching it would hold the user at the wrong privilege level for the full minute after a
        // momentary outage.
        this._poracleApiProxy.Setup(p => p.GetConfigAsync()).ThrowsAsync(new HttpRequestException("down"));
        this._poracleApiProxy.Setup(p => p.GetAdminRolesAsync(It.IsAny<string>())).ThrowsAsync(new HttpRequestException("down"));
        this._webhookDelegateService.Setup(s => s.GetManagedWebhookIdsAsync(It.IsAny<string>())).ReturnsAsync([]);
        var sut = this.CreateSut();

        await sut.ResolveAsync("u1");
        await sut.ResolveAsync("u1");

        this._poracleApiProxy.Verify(p => p.GetAdminRolesAsync("u1"), Times.Exactly(2));
    }

    [Fact]
    public async Task AConfiguredAdminNeedsNoNetworkAndIsAlwaysResolved()
    {
        var roles = await this.CreateSut("u1,u2").ResolveAsync("u1");

        Assert.True(roles.IsAdmin);
        Assert.True(roles.Resolved);
        this._poracleApiProxy.Verify(p => p.GetConfigAsync(), Times.Never);
    }

    [Fact]
    public async Task AGenuineNonAdminIsResolvedAndCached()
    {
        // The legitimate-case-still-passes half: a clean "no" must still be a usable answer, and must
        // still be cached.
        this._poracleApiProxy.Setup(p => p.GetConfigAsync()).ReturnsAsync((PoracleConfig?)null!);
        this._poracleApiProxy.Setup(p => p.GetAdminRolesAsync(It.IsAny<string>())).ReturnsAsync("{}");
        this._webhookDelegateService.Setup(s => s.GetManagedWebhookIdsAsync(It.IsAny<string>())).ReturnsAsync([]);
        var sut = this.CreateSut();

        var roles = await sut.ResolveAsync("u1");
        await sut.ResolveAsync("u1");

        Assert.False(roles.IsAdmin);
        Assert.True(roles.Resolved);
        this._poracleApiProxy.Verify(p => p.GetAdminRolesAsync("u1"), Times.Once);
    }
}
