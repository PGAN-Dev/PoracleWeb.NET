using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Filters;

/// <summary>
/// Whose block the filter checks. Under impersonation the <c>userId</c> claim names the destination being
/// managed, so checking it refused every call an admin or delegate made to clean up a blocked destination,
/// while a blocked impersonator was never checked at all. See #1038.
/// </summary>
public class BlockedAccountFilterTests
{
    private const string Admin = "111111111111111111";
    private const string Delegate = "222222222222222222";
    private const string Member = "333333333333333333";
    private const string Webhook = "https://discord.com/api/webhooks/123/secret-token";

    private readonly Mock<IHumanService> _humans = new();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    private void Blocked(params string[] ids)
    {
        this._humans.Setup(h => h.GetByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((string id) => new Human { Id = id, AdminDisable = ids.Contains(id) ? 1 : 0 });
    }

    private async Task<(bool NextCalled, IActionResult? Result)> RunAsync(string userId, string? impersonatedBy,
        bool isAdmin = false, string path = "/api/monsters")
    {
        var claims = new List<Claim> { new("userId", userId), new("isAdmin", isAdmin ? "true" : "false") };
        if (impersonatedBy is not null)
        {
            claims.Add(new Claim("impersonatedBy", impersonatedBy));
        }

        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) };
        http.Request.Path = path;
        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var context = new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), controller: new object());

        var filter = new BlockedAccountFilter(this._humans.Object, this._cache);
        var nextCalled = false;
        await filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, [], controller: new object()));
        });

        return (nextCalled, context.Result);
    }

    private static void AssertForbidden((bool NextCalled, IActionResult? Result) outcome)
    {
        Assert.False(outcome.NextCalled);
        var result = Assert.IsType<ObjectResult>(outcome.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    private static void AssertAllowed((bool NextCalled, IActionResult? Result) outcome)
    {
        Assert.True(outcome.NextCalled);
        Assert.Null(outcome.Result);
    }

    [Fact]
    public async Task AdminMayManageABlockedDestination()
    {
        this.Blocked(Webhook);

        AssertAllowed(await this.RunAsync(Webhook, impersonatedBy: Admin, isAdmin: true));
    }

    [Fact]
    public async Task DelegateMayManageABlockedDestination()
    {
        this.Blocked(Webhook);

        AssertAllowed(await this.RunAsync(Webhook, impersonatedBy: Delegate));
    }

    [Fact]
    public async Task BlockedImpersonatorIsRefused()
    {
        this.Blocked(Delegate);

        AssertForbidden(await this.RunAsync(Webhook, impersonatedBy: Delegate));
    }

    [Fact]
    public async Task BlockedImpersonatorIsLookedUpNotTheTarget()
    {
        this.Blocked(Delegate);

        await this.RunAsync(Webhook, impersonatedBy: Delegate);

        this._humans.Verify(h => h.GetByIdAsync(Delegate), Times.Once);
        this._humans.Verify(h => h.GetByIdAsync(Webhook), Times.Never);
    }

    [Fact]
    public async Task OrdinaryBlockedUserIsRefused()
    {
        this.Blocked(Member);

        AssertForbidden(await this.RunAsync(Member, impersonatedBy: null));
    }

    [Fact]
    public async Task OrdinaryUnblockedUserIsAllowed()
    {
        this.Blocked();

        AssertAllowed(await this.RunAsync(Member, impersonatedBy: null));
    }

    [Fact]
    public async Task AuthEndpointsStayOpenToABlockedImpersonator()
    {
        this.Blocked(Delegate);

        AssertAllowed(await this.RunAsync(Webhook, impersonatedBy: Delegate, path: "/api/auth/me"));
    }

    [Fact]
    public async Task ImpersonatorBlockIsReadFromTheCacheKeyAdminInvalidates()
    {
        // AdminController drops blocked:{id} on block and unblock, so the impersonator's answer must live
        // under the impersonator's own id for an unblock to take effect at once.
        this.Blocked(Delegate);
        AssertForbidden(await this.RunAsync(Webhook, impersonatedBy: Delegate));

        this.Blocked();
        this._cache.Remove($"blocked:{Delegate}");

        AssertAllowed(await this.RunAsync(Webhook, impersonatedBy: Delegate));
    }
}
