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
/// Sends account requests through the real pipeline, so model binding and validation run.
/// </summary>
/// <remarks>
/// <c>PlayerAccountControllerTests</c> calls the actions directly and never validates a body. A record
/// bound through its primary constructor takes its validation metadata from the constructor parameters;
/// attributes targeted at the properties make MVC throw while validating, and every write answered 500.
/// </remarks>
public sealed class PlayerAccountPipelineTests : IDisposable
{
    private const string UserId = "pipeline-trainer";

    private static readonly JwtSettings Jwt = new()
    {
        Secret = "integration-test-jwt-secret-at-least-32-chars",
        Issuer = "PoracleWeb",
        Audience = "PoracleWeb.App",
        ExpirationMinutes = 1440,
    };

    private readonly Mock<IPoracleAccountProxy> _proxy = new();
    private readonly Factory _factory;

    public PlayerAccountPipelineTests()
    {
        this._proxy
            .Setup(p => p.AddAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlayerAccount("Ab9cD", "Main", DateTimeOffset.UnixEpoch));
        this._factory = new Factory(this._proxy);
    }

    public void Dispose() => this._factory.Dispose();

    private static string Token() => new JwtService(Options.Create(Jwt)).GenerateToken(
        new UserInfo { Id = UserId, Username = UserId, Type = "discord:user", ProfileNo = 1, Enabled = true });

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object body)
    {
        using var client = this._factory.CreateClient();
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task AValidAddReachesTheProxy()
    {
        using var response = await this.SendAsync(HttpMethod.Post, "/api/accounts", new { playerId = "Ab9cD", label = "Main" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        this._proxy.Verify(p => p.AddAsync(UserId, "Ab9cD", "Main", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AValidRelabelReachesTheProxy()
    {
        using var response = await this.SendAsync(HttpMethod.Put, "/api/accounts/Ab9cD", new { label = "Alt" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        this._proxy.Verify(p => p.RelabelAsync(UserId, "Ab9cD", "Alt", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task APlayerIdOver256CharactersIsRefused()
    {
        using var response = await this.SendAsync(HttpMethod.Post, "/api/accounts", new { playerId = new string('a', 257) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        this._proxy.Verify(
            p => p.AddAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task APlayerIdOf256CharactersIsAccepted()
    {
        using var response = await this.SendAsync(HttpMethod.Post, "/api/accounts", new { playerId = new string('a', 256) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AMissingPlayerIdIsRefused()
    {
        using var response = await this.SendAsync(HttpMethod.Post, "/api/accounts", new { label = "Main" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ALabelOver64CharactersIsRefusedOnAddAndRelabel()
    {
        var label = new string('l', 65);

        using var add = await this.SendAsync(HttpMethod.Post, "/api/accounts", new { playerId = "Ab9cD", label });
        using var relabel = await this.SendAsync(HttpMethod.Put, "/api/accounts/Ab9cD", new { label });

        Assert.Equal(HttpStatusCode.BadRequest, add.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, relabel.StatusCode);
    }

    [Fact]
    public async Task ALabelOf64CharactersIsAccepted()
    {
        using var response = await this.SendAsync(HttpMethod.Put, "/api/accounts/Ab9cD", new { label = new string('l', 64) });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private sealed class Factory(Mock<IPoracleAccountProxy> proxy) : WebApplicationFactory<Program>
    {
        private const string DeadDb = "Server=127.0.0.1;Port=1;Database=none;User=none;Password=none";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.UseEnvironment(Environments.Production);
            builder.UseSetting("ConnectionStrings:PoracleDb", DeadDb);
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

                var schema = new Mock<IPoracleV2SchemaService>();
                schema
                    .Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new PoracleV2Capabilities { PlayerAccounts = true });
                var config = new Mock<IPersonalShinyConfigService>();
                config.Setup(c => c.GetAsync()).ReturnsAsync(new PersonalShinyConfig(true, true));

                services.RemoveAll<IPoracleAccountProxy>();
                services.AddScoped(_ => proxy.Object);
                services.RemoveAll<IPoracleV2SchemaService>();
                services.AddScoped(_ => schema.Object);
                services.RemoveAll<IPersonalShinyConfigService>();
                services.AddScoped(_ => config.Object);
            });
        }
    }
}
