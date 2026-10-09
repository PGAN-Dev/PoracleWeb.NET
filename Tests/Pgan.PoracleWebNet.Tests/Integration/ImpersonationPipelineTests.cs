using System.Net;
using System.Net.Http.Headers;
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
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Integration;

/// <summary>
/// Drives an impersonation token through the real authentication pipeline, so the check is proven to run
/// on every request rather than only inside the class that implements it.
/// </summary>
/// <remarks>
/// Revoking a delegate's grant stopped them minting a new impersonation token, and did nothing to the one
/// they already held: it kept full read and write access over the webhook for its 24-hour life. The
/// endpoint used here is an ordinary authenticated read with no database behind it, so the only thing
/// deciding 200 against 401 is the token check.
/// </remarks>
public sealed class ImpersonationPipelineTests : IDisposable
{
    private const string Path = "/api/settings/upstream-disabled";
    private const string Webhook = "pweb-test-webhook";
    private const string Delegate = "pweb-test-delegate";

    private static readonly JwtSettings Jwt = new()
    {
        Secret = "integration-test-jwt-secret-at-least-32-chars",
        Issuer = "PoracleWeb",
        Audience = "PoracleWeb.App",
        ExpirationMinutes = 1440,
    };

    private readonly Mock<IUserRoleResolver> _resolver = new();
    private readonly Factory _factory;

    public ImpersonationPipelineTests() => this._factory = new Factory(this._resolver);

    public void Dispose() => this._factory.Dispose();

    private static string ImpersonationToken() => new JwtService(Options.Create(Jwt)).GenerateImpersonationToken(
        new UserInfo { Id = Webhook, Username = Webhook, Type = "webhook", ProfileNo = 1, Enabled = true },
        Delegate);

    private async Task<HttpStatusCode> GetAsync(string token)
    {
        using var client = this._factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task ADelegateWhoStillHoldsTheGrantIsServed()
    {
        this._resolver.Setup(r => r.ResolveAsync(Delegate)).ReturnsAsync(new UserRoles(false, [Webhook]));

        Assert.Equal(HttpStatusCode.OK, await this.GetAsync(ImpersonationToken()));
    }

    [Fact]
    public async Task ARevokedDelegatesTokenIsRefusedOnTheNextRequest()
    {
        var token = ImpersonationToken();
        this._resolver.Setup(r => r.ResolveAsync(Delegate)).ReturnsAsync(new UserRoles(false, [Webhook]));
        Assert.Equal(HttpStatusCode.OK, await this.GetAsync(token));

        this._resolver.Setup(r => r.ResolveAsync(Delegate)).ReturnsAsync(new UserRoles(false, null));

        Assert.Equal(HttpStatusCode.Unauthorized, await this.GetAsync(token));
    }

    [Fact]
    public async Task AnOrdinaryTokenNeverConsultsTheResolver()
    {
        var token = new JwtService(Options.Create(Jwt)).GenerateToken(
            new UserInfo { Id = Delegate, Username = Delegate, Type = "discord:user", ProfileNo = 1, Enabled = true });

        Assert.Equal(HttpStatusCode.OK, await this.GetAsync(token));
        this._resolver.Verify(r => r.ResolveAsync(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// While the sources are down the resolver answers "unresolved" and caches nothing, so every
    /// impersonated request re-asked them and waited out their timeouts: with PoracleNG black-holed on the
    /// test bed that was minutes per request, against milliseconds for an ordinary session. Stood in for
    /// here by a resolve that takes six seconds to come back unresolved.
    /// </summary>
    [Fact]
    public async Task AnImpersonatedRequestDuringAnOutageIsNotHeldForTheSourcesTimeouts()
    {
        var calls = 0;
        this._resolver.Setup(r => r.ResolveAsync(Delegate)).Returns(async () =>
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(TimeSpan.FromSeconds(6));
            return new UserRoles(false, null, Resolved: false);
        });
        var token = ImpersonationToken();

        // Boot the host first, and time an ordinary request, so only the check's own cost is compared.
        var ordinaryToken = new JwtService(Options.Create(Jwt)).GenerateToken(
            new UserInfo { Id = Delegate, Username = Delegate, Type = "discord:user", ProfileNo = 1, Enabled = true });
        await this.GetAsync(ordinaryToken);
        var ordinary = System.Diagnostics.Stopwatch.StartNew();
        await this.GetAsync(ordinaryToken);
        ordinary.Stop();

        var first = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(HttpStatusCode.OK, await this.GetAsync(token));
        first.Stop();
        var second = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(HttpStatusCode.OK, await this.GetAsync(token));
        second.Stop();

        // The two-second budget, plus slack; the outage itself is six.
        Assert.True(first.Elapsed - ordinary.Elapsed < TimeSpan.FromSeconds(3.5), $"first request took {first.Elapsed}, an ordinary one {ordinary.Elapsed}");
        Assert.True(second.Elapsed - ordinary.Elapsed < TimeSpan.FromSeconds(1), $"second request took {second.Elapsed}, an ordinary one {ordinary.Elapsed}");
        Assert.Equal(1, Volatile.Read(ref calls));
    }

    /// <summary>
    /// The legitimate half: the pause ends the moment the sources answer, so a grant revoked during an
    /// outage still ends the session on the next request after it.
    /// </summary>
    [Fact]
    public async Task ARevocationLandsAsSoonAsTheSourcesAnswerAgain()
    {
        var outage = new TaskCompletionSource<UserRoles>(TaskCreationOptions.RunContinuationsAsynchronously);
        this._resolver.Setup(r => r.ResolveAsync(Delegate)).Returns(() => outage.Task);
        var token = ImpersonationToken();

        // Boot the host, then: without the budget this request waits on the outage indefinitely.
        await this.GetAsync(new JwtService(Options.Create(Jwt)).GenerateToken(
            new UserInfo { Id = Delegate, Username = Delegate, Type = "discord:user", ProfileNo = 1, Enabled = true }));
        var first = this.GetAsync(token);
        Assert.Same(first, await Task.WhenAny(first, Task.Delay(TimeSpan.FromSeconds(15))));
        Assert.Equal(HttpStatusCode.OK, await first);

        // The sources come back, and the answer is that the grant is gone.
        this._resolver.Setup(r => r.ResolveAsync(Delegate)).ReturnsAsync(new UserRoles(false, null));
        outage.SetResult(new UserRoles(false, null));
        await Task.Delay(100);

        Assert.Equal(HttpStatusCode.Unauthorized, await this.GetAsync(token));
    }

    private sealed class Factory(Mock<IUserRoleResolver> resolver) : WebApplicationFactory<Program>
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

                services.RemoveAll<IUserRoleResolver>();
                services.AddScoped(_ => resolver.Object);
            });
        }
    }
}
