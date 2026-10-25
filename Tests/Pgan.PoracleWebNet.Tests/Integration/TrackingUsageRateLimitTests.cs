using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Services;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Integration;

/// <summary>
/// <c>GET /api/location/place-usage</c> asks PoracleNG to render a description for every rule on the
/// active profile, and some accounts hold tens of thousands. Both usage reads are bounded per user,
/// never globally: one person clicking Delete on their places must not use up anyone else's checks.
/// </summary>
public sealed class TrackingUsageRateLimitTests : IDisposable
{
    private const int Limit = 20;

    private static readonly JwtSettings Jwt = new()
    {
        Secret = "integration-test-jwt-secret-at-least-32-chars",
        Issuer = "PoracleWeb",
        Audience = "PoracleWeb.App",
        ExpirationMinutes = 1440,
    };

    private readonly Mock<IPoracleTrackingProxy> _tracking = new();
    private readonly Factory _factory;

    public TrackingUsageRateLimitTests()
    {
        var empty = JsonDocument.Parse("{}").RootElement;
        this._tracking.Setup(t => t.GetAllTrackingAsync(It.IsAny<string>())).ReturnsAsync(empty);
        this._tracking.Setup(t => t.GetAllTrackingWithDescriptionsAsync(It.IsAny<string>())).ReturnsAsync(empty);
        this._factory = new Factory(this._tracking);
    }

    public void Dispose() => this._factory.Dispose();

    private static string TokenFor(string userId) => new JwtService(Options.Create(Jwt)).GenerateToken(
        new UserInfo { Id = userId, Username = userId, Type = "discord:user", ProfileNo = 1, Enabled = true });

    private async Task<HttpStatusCode> GetAsync(HttpClient client, string path, string userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TokenFor(userId));
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task AUserCheckingSeveralPlacesInARowIsServedEveryTime()
    {
        using var client = this._factory.CreateClient();

        for (var i = 0; i < Limit; i++)
        {
            var path = i % 2 == 0 ? "/api/location/place-usage?label=home" : "/api/location/pin-usage";
            Assert.Equal(HttpStatusCode.OK, await this.GetAsync(client, path, "user-a"));
        }
    }

    [Fact]
    public async Task ALoopIsStoppedForThatUserAndNobodyElse()
    {
        using var client = this._factory.CreateClient();
        for (var i = 0; i < Limit; i++)
        {
            await this.GetAsync(client, "/api/location/place-usage?label=home", "user-a");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await this.GetAsync(client, "/api/location/place-usage?label=home", "user-a"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await this.GetAsync(client, "/api/location/pin-usage", "user-a"));
        Assert.Equal(HttpStatusCode.OK, await this.GetAsync(client, "/api/location/place-usage?label=home", "user-b"));
        this._tracking.Verify(t => t.GetAllTrackingWithDescriptionsAsync("user-a"), Times.Exactly(Limit));
    }

    private sealed class Factory(Mock<IPoracleTrackingProxy> tracking) : WebApplicationFactory<Program>
    {
        private const string DeadDb = "Server=127.0.0.1;Port=1;Database=none;User=none;Password=none";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.UseEnvironment(Environments.Production);
            builder.UseSetting("ConnectionStrings:PoracleWebDb", DeadDb);
            builder.UseSetting("Jwt:Secret", Jwt.Secret);
            builder.UseSetting("Jwt:Issuer", Jwt.Issuer);
            builder.UseSetting("Jwt:Audience", Jwt.Audience);
            builder.UseSetting("Discord:ClientId", "test-client-id");
            builder.UseSetting("Discord:ClientSecret", "test-client-secret");
            builder.UseSetting("Cors:AllowedOrigins:0", "https://allowed.example");
            builder.UseSetting("Poracle:ApiAddress", "http://127.0.0.1:1");
            builder.UseSetting("Poracle:ApiSecret", "secret");

            builder.ConfigureTestServices(services =>
            {
                foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                {
                    services.Remove(hosted);
                }

                var gate = new Mock<IFeatureGate>();
                gate.Setup(g => g.IsEnabledAsync(It.IsAny<string>())).ReturnsAsync(true);
                services.RemoveAll<IFeatureGate>();
                services.AddScoped(_ => gate.Object);
                // The blocked-account check reads the human on every request; against a dead PoracleNG
                // address each read waits out a refused connection.
                var humans = new Mock<IHumanService>();
                humans.Setup(h => h.GetByIdAsync(It.IsAny<string>())).ReturnsAsync(new Human { Id = "user" });
                services.RemoveAll<IHumanService>();
                services.AddScoped(_ => humans.Object);
                // These requests never touch a link; the real resolver would read the link table.
                services.RemoveAll<ILinkedAccount>();
                services.AddScoped(_ => Pgan.PoracleWebNet.Tests.Helpers.LinkedTestKit.Unlinked());
                services.RemoveAll<IPoracleTrackingProxy>();
                services.AddScoped(_ => tracking.Object);
            });
        }
    }
}
