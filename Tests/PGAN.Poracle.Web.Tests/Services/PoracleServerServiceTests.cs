using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PGAN.Poracle.Web.Core.Services;

namespace PGAN.Poracle.Web.Tests.Services;

public class PoracleServerServiceTests
{
    private static (PoracleServerService sut, MockHttpMessageHandler handler) CreateService(
        Dictionary<string, string?> configValues)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var logger = new Mock<ILogger<PoracleServerService>>();

        var sut = new PoracleServerService(httpClient, configuration, logger.Object);
        return (sut, handler);
    }

    private static Dictionary<string, string?> TwoServerConfig() => new()
    {
        ["Poracle:SshKeyPath"] = "/app/ssh_key",
        ["Poracle:Servers:0:Name"] = "Server1",
        ["Poracle:Servers:0:Host"] = "10.0.0.1",
        ["Poracle:Servers:0:ApiAddress"] = "http://10.0.0.1:4321",
        ["Poracle:Servers:0:SshUser"] = "root",
        ["Poracle:Servers:1:Name"] = "Server2",
        ["Poracle:Servers:1:Host"] = "10.0.0.2",
        ["Poracle:Servers:1:ApiAddress"] = "http://10.0.0.2:4321",
        ["Poracle:Servers:1:SshUser"] = "admin",
    };

    // --- GetServersAsync ---

    [Fact]
    public async Task GetServersAsyncReturnsEmptyWhenNoServersConfigured()
    {
        var (sut, _) = CreateService([]);

        var result = await sut.GetServersAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetServersAsyncReturnsStatusForEachServer()
    {
        var (sut, handler) = CreateService(TwoServerConfig());
        handler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK);

        var result = await sut.GetServersAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("Server1", result[0].Name);
        Assert.Equal("10.0.0.1", result[0].Host);
        Assert.True(result[0].Online);
        Assert.Equal("Server2", result[1].Name);
        Assert.True(result[1].Online);
    }

    [Fact]
    public async Task GetServersAsyncMarksOnlineForAnyHttpResponse()
    {
        // Any HTTP response (even 401/500) means the server is running
        var (sut, handler) = CreateService(TwoServerConfig());
        handler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        var result = await sut.GetServersAsync();

        Assert.Equal(2, result.Count);
        Assert.True(result[0].Online);
        Assert.True(result[1].Online);
    }

    [Fact]
    public async Task GetServersAsyncMarksOfflineWhenHttpThrows()
    {
        var (sut, handler) = CreateService(TwoServerConfig());
        handler.ExceptionFactory = _ => new HttpRequestException("connection refused");

        var result = await sut.GetServersAsync();

        Assert.Equal(2, result.Count);
        Assert.False(result[0].Online);
        Assert.False(result[1].Online);
    }

    [Fact]
    public async Task GetServersAsyncSetsCheckedAt()
    {
        var (sut, handler) = CreateService(TwoServerConfig());
        handler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK);

        var before = DateTime.UtcNow;
        var result = await sut.GetServersAsync();
        var after = DateTime.UtcNow;

        foreach (var status in result)
        {
            Assert.InRange(status.CheckedAt, before, after);
        }
    }

    [Fact]
    public async Task GetServersAsyncSkipsEntriesWithEmptyHost()
    {
        var config = new Dictionary<string, string?>
        {
            ["Poracle:Servers:0:Name"] = "NoHost",
            ["Poracle:Servers:0:Host"] = "",
            ["Poracle:Servers:0:ApiAddress"] = "http://localhost",
            ["Poracle:Servers:1:Name"] = "HasHost",
            ["Poracle:Servers:1:Host"] = "10.0.0.1",
            ["Poracle:Servers:1:ApiAddress"] = "http://10.0.0.1:4321",
        };
        var (sut, handler) = CreateService(config);
        handler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK);

        var result = await sut.GetServersAsync();

        Assert.Single(result);
        Assert.Equal("HasHost", result[0].Name);
    }

    // --- RestartServerAsync ---

    [Fact]
    public async Task RestartServerAsyncThrowsWhenHostNotFound()
    {
        var (sut, _) = CreateService(TwoServerConfig());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.RestartServerAsync("unknown-host"));
        Assert.Contains("not found in configuration", ex.Message);
    }

    [Fact]
    public async Task RestartServerAsyncThrowsOnInvalidHostnameCharacters()
    {
        var config = new Dictionary<string, string?>
        {
            ["Poracle:Servers:0:Name"] = "Bad",
            ["Poracle:Servers:0:Host"] = "host; rm -rf /",
            ["Poracle:Servers:0:ApiAddress"] = "http://localhost",
        };
        var (sut, _) = CreateService(config);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => sut.RestartServerAsync("host; rm -rf /"));
        Assert.Contains("Invalid characters", ex.Message);
    }

    [Fact]
    public async Task RestartServerAsyncThrowsOnInvalidSshUser()
    {
        var config = new Dictionary<string, string?>
        {
            ["Poracle:Servers:0:Name"] = "Bad",
            ["Poracle:Servers:0:Host"] = "10.0.0.1",
            ["Poracle:Servers:0:ApiAddress"] = "http://localhost",
            ["Poracle:Servers:0:SshUser"] = "root && whoami",
        };
        var (sut, _) = CreateService(config);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => sut.RestartServerAsync("10.0.0.1"));
        Assert.Contains("Invalid characters", ex.Message);
    }

    // --- RestartAllAsync ---

    [Fact]
    public async Task RestartAllAsyncReturnsEmptyWhenNoServers()
    {
        var (sut, _) = CreateService([]);

        var result = await sut.RestartAllAsync();

        Assert.Empty(result);
    }

    // --- Helper ---

    public class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? ResponseFactory
        {
            get; set;
        }
        public Func<HttpRequestMessage, Exception>? ExceptionFactory
        {
            get; set;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (this.ExceptionFactory is not null)
            {
                throw this.ExceptionFactory(request);
            }

            var response = this.ResponseFactory?.Invoke(request)
                ?? new HttpResponseMessage(HttpStatusCode.OK);
            return Task.FromResult(response);
        }
    }
}
