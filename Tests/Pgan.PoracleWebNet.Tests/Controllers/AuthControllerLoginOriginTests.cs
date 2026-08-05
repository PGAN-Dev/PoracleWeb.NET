using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Api.Services.Oidc;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Tests.Controllers;

/// <summary>
/// Tests for the frontend-origin recovery in AuthController.DiscordLogin — the Referer read
/// that decides which origin the user is sent back to after the provider callback, stashed in
/// the oauth_origin cookie.
///
/// This behavior is why the app's Referrer-Policy is "same-origin" and not "no-referrer"
/// (issue #383): a policy that blanks the same-origin Referer would make every case below
/// fall back to the API's own origin. See SecurityHeaders.ReferrerPolicy.
/// </summary>
public class AuthControllerLoginOriginTests
{
    private const string SelfOrigin = "https://alerts.example.net";

    [Fact]
    public async Task DiscordLoginUsesRefererOriginWhenItIsAnAllowedCorsOrigin()
    {
        var controller = CreateController(
            allowedOrigins: ["https://app.example.net", SelfOrigin],
            referer: "https://app.example.net/auth/login");

        await controller.DiscordLogin();

        Assert.Equal("https://app.example.net", ReadOriginCookie(controller));
    }

    /// <summary>
    /// The shared-host production topology: the SPA is served by this same host, so the
    /// referrer is same-origin. "same-origin" policy sends the full URL here, so the header
    /// is present and the recovered origin matches self.
    /// </summary>
    [Fact]
    public async Task DiscordLoginUsesRefererOriginWhenItMatchesSelfAndNoCorsOriginsConfigured()
    {
        var controller = CreateController(
            allowedOrigins: [],
            referer: $"{SelfOrigin}/auth/login");

        await controller.DiscordLogin();

        Assert.Equal(SelfOrigin, ReadOriginCookie(controller));
    }

    /// <summary>
    /// The no-referrer scenario, asserted explicitly: with no Referer to read, the origin
    /// silently degrades to this host. Harmless when the SPA shares the host, wrong when it
    /// doesn't — which is the regression a "no-referrer" policy would introduce everywhere.
    /// </summary>
    [Fact]
    public async Task DiscordLoginFallsBackToSelfOriginWhenRefererIsAbsent()
    {
        var controller = CreateController(allowedOrigins: ["https://app.example.net"], referer: null);

        await controller.DiscordLogin();

        Assert.Equal(SelfOrigin, ReadOriginCookie(controller));
    }

    [Fact]
    public async Task DiscordLoginIgnoresRefererOriginThatIsNotAllowed()
    {
        var controller = CreateController(
            allowedOrigins: ["https://app.example.net"],
            referer: "https://evil.example.com/auth/login");

        await controller.DiscordLogin();

        Assert.Equal(SelfOrigin, ReadOriginCookie(controller));
    }

    [Fact]
    public async Task DiscordLoginIgnoresRefererThatIsNotAnAbsoluteUri()
    {
        var controller = CreateController(allowedOrigins: [], referer: "/auth/login");

        await controller.DiscordLogin();

        Assert.Equal(SelfOrigin, ReadOriginCookie(controller));
    }

    private static AuthController CreateController(string[] allowedOrigins, string? referer)
    {
        var settings = new Dictionary<string, string?>();
        for (var i = 0; i < allowedOrigins.Length; i++)
        {
            settings[$"Cors:AllowedOrigins:{i}"] = allowedOrigins[i];
        }

        var controller = new AuthController(
            new Mock<IHumanService>().Object,
            new Mock<IPoracleApiProxy>().Object,
            new Mock<IPoracleHumanProxy>().Object,
            new Mock<ISiteSettingService>().Object,
            new Mock<IWebhookDelegateService>().Object,
            new Mock<IJwtService>().Object,
            new Mock<IOidcClient>().Object,
            new Mock<IOidcSessionService>().Object,
            Options.Create(new DiscordSettings { ClientId = "test-id", ClientSecret = "test-secret" }),
            Options.Create(new TelegramSettings()),
            Options.Create(new OidcSettings()),
            Options.Create(new PoracleSettings()),
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            new Mock<ILogger<AuthController>>().Object);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("alerts.example.net");
        if (referer != null)
        {
            httpContext.Request.Headers.Referer = referer;
        }

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static string? ReadOriginCookie(ControllerBase controller)
    {
        const string Name = "oauth_origin=";

        var cookie = controller.Response.Headers.SetCookie
            .FirstOrDefault(c => c != null && c.StartsWith(Name, StringComparison.Ordinal));
        if (cookie == null)
        {
            return null;
        }

        var value = cookie[Name.Length..];
        var end = value.IndexOf(';', StringComparison.Ordinal);
        if (end >= 0)
        {
            value = value[..end];
        }

        return Uri.UnescapeDataString(value);
    }
}
