using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Pgan.PoracleWebNet.Tests.Configuration;

/// <summary>
/// Every PoracleNG proxy is a typed <c>HttpClient</c>, and the factory's own handlers log each request's URL at
/// Information under <c>System.Net.Http.HttpClient.{name}.LogicalHandler</c> and <c>.ClientHandler</c>. .NET
/// redacts the query string there, not the path, and a webhook's v2 path carries its URL, token included. The
/// shipped <c>appsettings.json</c> must keep that category above Information. The same file keeps ASP.NET Core's
/// own request lines out: the admin routes take a destination's id in the query string or the body, and
/// <c>Request starting</c> and <c>Route matched with {id = ...}</c> print both at Information. See #1038.
/// </summary>
public class HttpClientLoggingConfigurationTests
{
    private const string Token = "SECRETTOKENVALUE";
    private static readonly string WebhookPath =
        "/api/v2/humans/" + Uri.EscapeDataString($"https://discord.com/api/webhooks/123/{Token}") + "/tracking/pokemon";

    [Fact]
    public async Task ShippedSettingsKeepHttpClientRequestLinesOutOfTheLog()
    {
        var (messages, provider) = BuildServices(ShippedSettings());
        using (provider)
        {
            var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("poracle");
            await client.GetAsync(new Uri("http://poracle.test" + WebhookPath));
        }

        Assert.DoesNotContain(messages, m => m.Contains(Token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithoutTheOverrideTheTokenWouldBeLogged()
    {
        // Proves the test above is looking at the right lines: at the framework default, the path is logged.
        var (messages, provider) = BuildServices(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Logging:LogLevel:Default"] = "Information" })
            .Build());
        using (provider)
        {
            var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("poracle");
            await client.GetAsync(new Uri("http://poracle.test" + WebhookPath));
        }

        Assert.Contains(messages, m => m.Contains(Token, StringComparison.Ordinal));
    }

    [Fact]
    public void ShippedSettingsStillLogTheApplicationAtInformation()
    {
        var (_, provider) = BuildServices(ShippedSettings());
        using (provider)
        {
            var factory = provider.GetRequiredService<ILoggerFactory>();

            Assert.True(factory.CreateLogger("Pgan.PoracleWebNet.Api.Services.UserRoleResolver").IsEnabled(LogLevel.Information));
            Assert.True(factory.CreateLogger("System.Net.Http.HttpClient.poracle.LogicalHandler").IsEnabled(LogLevel.Warning));
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task ShippedSettingsKeepTheTokenOutOfTheLogWhenPoracleAnswersWithAnError(HttpStatusCode status)
    {
        var (messages, provider) = BuildServices(ShippedSettings(), new StubHandler(status));
        using (provider)
        {
            var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("poracle");
            var response = await client.GetAsync(new Uri("http://poracle.test" + WebhookPath));
            Assert.Equal(status, response.StatusCode);
        }

        Assert.DoesNotContain(messages, m => m.Contains(Token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShippedSettingsKeepTheTokenOutOfTheLogWhenPoracleCannotBeReached()
    {
        var (messages, provider) = BuildServices(ShippedSettings(), new ThrowingHandler());
        using (provider)
        {
            var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("poracle");
            await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(new Uri("http://poracle.test" + WebhookPath)));
        }

        Assert.DoesNotContain(messages, m => m.Contains(Token, StringComparison.Ordinal));
    }

    /// <summary>
    /// The categories that print a request's path, query or route values. The admin endpoints that take a
    /// destination's id (<c>users/by-id?id=</c>, <c>DELETE users?id=</c>, <c>webhook-delegates?webhookId=</c>)
    /// carry a webhook's URL there, so these must stay above Information in the shipped settings.
    /// </summary>
    [Theory]
    [InlineData("Microsoft.AspNetCore.Hosting.Diagnostics")]
    [InlineData("Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker")]
    [InlineData("Microsoft.AspNetCore.Routing.EndpointMiddleware")]
    public void ShippedSettingsKeepAspNetRequestLinesOutOfTheLog(string category)
    {
        var (_, provider) = BuildServices(ShippedSettings());
        using (provider)
        {
            var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(category);

            Assert.False(logger.IsEnabled(LogLevel.Information));
            Assert.True(logger.IsEnabled(LogLevel.Error));
        }
    }

    private static IConfiguration ShippedSettings()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Pgan.PoracleWebNet.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(dir.FullName, "Applications", "Pgan.PoracleWebNet.Api", "appsettings.json"))
            .Build();
    }

    private static (List<string> Messages, ServiceProvider Provider) BuildServices(IConfiguration configuration, HttpMessageHandler? handler = null)
    {
        var messages = new List<string>();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConfiguration(configuration.GetSection("Logging")).AddProvider(new CapturingProvider(messages)));
        services.AddHttpClient("poracle").ConfigurePrimaryHttpMessageHandler(() => handler ?? new StubHandler(HttpStatusCode.OK));
        return (messages, services.BuildServiceProvider());
    }

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused (poracle.test:80)");
    }

    private sealed class CapturingProvider(List<string> messages) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Logger(messages);

        public void Dispose()
        {
        }

        private sealed class Logger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (messages)
                {
                    messages.Add(formatter(state, exception));
                }
            }
        }
    }
}
