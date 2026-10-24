using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pgan.PoracleWebNet.Api.Services;

namespace Pgan.PoracleWebNet.Tests.Integration;

/// <summary>
/// A Discord Cancel comes back with <c>?error=access_denied</c> and no <c>code</c>. Through the real MVC
/// pipeline, a non-nullable <c>code</c> parameter answered an automatic 400 before the action ran, so the
/// <c>link_cancelled</c> and <c>missing_code</c> branches were unreachable.
/// </summary>
public sealed class DiscordCancelPipelineTests : IDisposable
{
    private const string Origin = "https://app.example";
    private const string State = "state-100000000000000001";

    private readonly Factory _factory = new();

    public void Dispose() => this._factory.Dispose();

    private async Task<HttpResponseMessage> CallbackAsync(string query, string? linkCookie)
    {
        using var client = this._factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/discord/callback" + query);
        var cookies = $"oauth_state={State}; oauth_origin={Origin}";
        if (linkCookie is not null)
        {
            cookies += $"; oauth_link={linkCookie}";
        }

        request.Headers.Add("Cookie", cookies);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task ACancelledLinkRedirectsToTheAccountPageWithLinkCancelled()
    {
        var provider = this._factory.Services.GetRequiredService<IDataProtectionProvider>();
        var ticket = AccountLinkTicket.Seal(provider, "100000000000000001", State, DateTimeOffset.UtcNow);

        using var response = await this.CallbackAsync($"?error=access_denied&state={State}", ticket);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"{Origin}/linked-accounts#link_error=link_cancelled", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task ACancelledSignInRedirectsToLoginWithMissingCode()
    {
        using var response = await this.CallbackAsync($"?error=access_denied&state={State}", null);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"{Origin}/login#error=missing_code", response.Headers.Location?.ToString());
    }

    /// <summary>The state guard is untouched: a bad state is still a 400, with or without a code.</summary>
    [Fact]
    public async Task AMismatchedStateIsStillRefused()
    {
        using var response = await this.CallbackAsync("?error=access_denied&state=other", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class Factory : WebApplicationFactory<Program>
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
            builder.UseSetting("Cors:AllowedOrigins:0", Origin);
            builder.UseSetting("Poracle:ApiAddress", "http://127.0.0.1:1");
            builder.UseSetting("Poracle:ApiSecret", "secret");

            builder.ConfigureTestServices(services =>
            {
                foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                {
                    services.Remove(hosted);
                }
            });
        }
    }
}
