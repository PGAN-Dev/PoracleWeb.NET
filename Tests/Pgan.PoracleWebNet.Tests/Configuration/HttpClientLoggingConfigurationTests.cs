using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Pgan.PoracleWebNet.Tests.Configuration;

/// <summary>
/// Every PoracleNG proxy is a typed <c>HttpClient</c>, and the factory's own handlers log each request's URL at
/// Information under <c>System.Net.Http.HttpClient.{name}.LogicalHandler</c> and <c>.ClientHandler</c>. .NET
/// redacts the query string there, not the path, and a webhook's v2 path carries its URL, token included. The
/// shipped <c>appsettings.json</c> must keep that category above Information. See #1038.
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

    private static (List<string> Messages, ServiceProvider Provider) BuildServices(IConfiguration configuration)
    {
        var messages = new List<string>();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConfiguration(configuration.GetSection("Logging")).AddProvider(new CapturingProvider(messages)));
        services.AddHttpClient("poracle").ConfigurePrimaryHttpMessageHandler(() => new StubHandler());
        return (messages, services.BuildServiceProvider());
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
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
