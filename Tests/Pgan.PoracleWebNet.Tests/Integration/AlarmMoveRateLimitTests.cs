using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Integration;

/// <summary>
/// A move reads and writes both humans of a pair, once per alarm. It is bounded per person (10 requests
/// in 60 seconds, each carrying a whole selection), never globally.
/// </summary>
public sealed class AlarmMoveRateLimitTests : IDisposable
{
    private const int Limit = 10;

    private static readonly JwtSettings Jwt = new()
    {
        Secret = "integration-test-jwt-secret-at-least-32-chars",
        Issuer = "PoracleWeb",
        Audience = "PoracleWeb.App",
        ExpirationMinutes = 1440,
    };

    private readonly Factory _factory = new();

    public void Dispose() => this._factory.Dispose();

    private static string TokenFor(string userId) => new JwtService(Options.Create(Jwt)).GenerateToken(
        new UserInfo { Id = userId, Username = userId, Type = "discord:user", ProfileNo = 1, Enabled = true });

    private static async Task<HttpStatusCode> MoveAsync(HttpClient client, string userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/alarm-move")
        {
            Content = JsonContent.Create(new { type = "raid", uids = new[] { 1 }, destination = "telegram" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TokenFor(userId));
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task ARunOfMovesWithinTheLimitIsNeverRefusedForRate()
    {
        using var client = this._factory.CreateClient();

        for (var i = 0; i < Limit; i++)
        {
            Assert.NotEqual(HttpStatusCode.TooManyRequests, await MoveAsync(client, "user-a"));
        }
    }

    [Fact]
    public async Task ALoopIsStoppedForThatUserAndNobodyElse()
    {
        using var client = this._factory.CreateClient();
        for (var i = 0; i < Limit; i++)
        {
            await MoveAsync(client, "user-a");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await MoveAsync(client, "user-a"));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, await MoveAsync(client, "user-b"));
    }

    private sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.UseEnvironment(Environments.Production);
            builder.UseSetting("ConnectionStrings:PoracleWebDb", "Server=127.0.0.1;Port=1;Database=none;User=none;Password=none");
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
                var humans = new Mock<IHumanService>();
                humans.Setup(h => h.GetByIdAsync(It.IsAny<string>())).ReturnsAsync(new Human { Id = "user" });
                services.RemoveAll<IHumanService>();
                services.AddScoped(_ => humans.Object);
                services.RemoveAll<ILinkedAccount>();
                services.AddScoped(_ => Pgan.PoracleWebNet.Tests.Helpers.LinkedTestKit.Unlinked());
            });
        }
    }
}
