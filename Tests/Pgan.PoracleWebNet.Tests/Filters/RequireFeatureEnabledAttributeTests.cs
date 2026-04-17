using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Pgan.PoracleWebNet.Api.Filters;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Tests.Filters;

/// <summary>
/// Verifies the resource filter behind <see cref="RequireFeatureEnabledAttribute"/>.
/// Closes the gap reported in #236: <c>disable_*</c> site settings must enforce server-side, not just hide nav.
/// The filter type is private, so we resolve it through DI the same way ASP.NET Core does at runtime.
/// </summary>
public class RequireFeatureEnabledAttributeTests
{
    private static (IAsyncResourceFilter filter, Mock<IFeatureGate> gate) BuildFilter(string disableKey)
    {
        var gate = new Mock<IFeatureGate>();
        var services = new ServiceCollection();
        services.AddSingleton(gate.Object);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var attribute = new RequireFeatureEnabledAttribute(disableKey);
        var filter = (IAsyncResourceFilter)attribute.CreateInstance(provider);
        return (filter, gate);
    }

    private static ResourceExecutingContext BuildContext(bool isAdmin)
    {
        var claims = new List<Claim> { new("isAdmin", isAdmin ? "true" : "false") };
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) };
        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new ResourceExecutingContext(actionContext, [], []);
    }

    [Fact]
    public async Task FeatureEnabledExecutesNextDelegate()
    {
        var (filter, gate) = BuildFilter("disable_mons");
        gate.Setup(g => g.IsEnabledAsync("disable_mons")).ReturnsAsync(true);
        var context = BuildContext(isAdmin: false);
        var nextCalled = false;

        await filter.OnResourceExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ResourceExecutedContext(context, []) { Result = null });
        });

        Assert.True(nextCalled);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task FeatureDisabledShortCircuitsWith403()
    {
        var (filter, gate) = BuildFilter("disable_mons");
        gate.Setup(g => g.IsEnabledAsync("disable_mons")).ReturnsAsync(false);
        var context = BuildContext(isAdmin: false);
        var nextCalled = false;

        await filter.OnResourceExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ResourceExecutedContext(context, []) { Result = null });
        });

        Assert.False(nextCalled);
        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    [Fact]
    public async Task AdminAlsoBlockedByDisabledFeature()
    {
        // The toggle means "nobody uses this feature" — including admins. If an admin needs the feature,
        // they flip the toggle off in Admin → Settings. Avoids the same UI/API mismatch that #236 was about.
        var (filter, gate) = BuildFilter("disable_mons");
        gate.Setup(g => g.IsEnabledAsync("disable_mons")).ReturnsAsync(false);
        var context = BuildContext(isAdmin: true);
        var nextCalled = false;

        await filter.OnResourceExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ResourceExecutedContext(context, []) { Result = null });
        });

        Assert.False(nextCalled);
        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    [Fact]
    public async Task MissingSettingTreatedAsEnabled()
    {
        // FeatureGate returns true for missing keys (since SiteSettingService.GetBoolAsync returns false).
        // New deployments must not 403 every alarm endpoint until an admin explicitly seeds the disable_* keys.
        var (filter, gate) = BuildFilter("disable_mons");
        gate.Setup(g => g.IsEnabledAsync("disable_mons")).ReturnsAsync(true);
        var context = BuildContext(isAdmin: false);
        var nextCalled = false;

        await filter.OnResourceExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ResourceExecutedContext(context, []) { Result = null });
        });

        Assert.True(nextCalled);
        Assert.Null(context.Result);
    }
}
