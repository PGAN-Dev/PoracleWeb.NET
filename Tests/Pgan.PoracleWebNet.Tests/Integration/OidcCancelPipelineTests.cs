using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Pgan.PoracleWebNet.Api.Services.Oidc;

namespace Pgan.PoracleWebNet.Tests.Integration;

/// <summary>
/// An OIDC Cancel comes back with <c>?error=access_denied</c> and no <c>code</c>. Through the real MVC
/// pipeline, a non-nullable <c>code</c> parameter answered an automatic 400 before the action ran, so the
/// missing-code redirect was unreachable. See #1057.
/// </summary>
public sealed class OidcCancelPipelineTests : IDisposable
{
    private const string Origin = "https://app.example";
    private const string State = "state-oidc-1";

    private readonly Mock<IOidcClient> _oidc = new();
    private readonly Factory _factory;

    public OidcCancelPipelineTests() => this._factory = new Factory(this._oidc.Object);

    public void Dispose() => this._factory.Dispose();

    private async Task<HttpResponseMessage> CallbackAsync(string query)
    {
        using var client = this._factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/oidc/callback" + query);
        request.Headers.Add("Cookie", $"oauth_state={State}; oauth_origin={Origin}");
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task ACancelledSignInRedirectsToLoginWithMissingCode()
    {
        using var response = await this.CallbackAsync($"?error=access_denied&state={State}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"{Origin}/login#error=missing_code", response.Headers.Location?.ToString());
    }

    /// <summary>The legitimate case: a request with a code still reaches the token exchange.</summary>
    [Fact]
    public async Task ARequestWithACodeStillReachesTheTokenExchange()
    {
        this._oidc.Setup(c => c.ExchangeCodeAsync("abc", It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((OidcTokenResult?)null);

        using var response = await this.CallbackAsync($"?code=abc&state={State}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"{Origin}/login#error=oidc_token_exchange_failed", response.Headers.Location?.ToString());
        this._oidc.Verify(c => c.ExchangeCodeAsync("abc", It.IsAny<string>(), It.IsAny<string?>()), Times.Once);
    }

    /// <summary>The state guard is untouched: a bad state is still a 400, with or without a code.</summary>
    [Fact]
    public async Task AMismatchedStateIsStillRefused()
    {
        using var response = await this.CallbackAsync("?error=access_denied&state=other");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class Factory(IOidcClient oidc) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.UseEnvironment(Environments.Production);
            builder.UseSetting("ConnectionStrings:PoracleWebDb", "Server=127.0.0.1;Port=1;Database=none;User=none;Password=none");
            builder.UseSetting("Jwt:Secret", "integration-test-jwt-secret-at-least-32-chars");
            builder.UseSetting("Jwt:Issuer", "PoracleWeb");
            builder.UseSetting("Jwt:Audience", "PoracleWeb.App");
            builder.UseSetting("Discord:ClientId", "test-client-id");
            builder.UseSetting("Discord:ClientSecret", "test-client-secret");
            builder.UseSetting("Oidc:Enabled", "true");
            builder.UseSetting("Oidc:AuthorizationUrl", "https://idp.example/authorize");
            builder.UseSetting("Oidc:TokenUrl", "https://idp.example/token");
            builder.UseSetting("Oidc:UserInfoUrl", "https://idp.example/userinfo");
            builder.UseSetting("Oidc:ClientId", "oidc-client");
            builder.UseSetting("Cors:AllowedOrigins:0", Origin);
            builder.UseSetting("Poracle:ApiAddress", "http://127.0.0.1:1");
            builder.UseSetting("Poracle:ApiSecret", "secret");

            builder.ConfigureTestServices(services =>
            {
                foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                {
                    services.Remove(hosted);
                }

                foreach (var d in services.Where(d => d.ServiceType == typeof(IOidcClient)).ToList())
                {
                    services.Remove(d);
                }

                services.AddSingleton(oidc);
            });
        }
    }
}
