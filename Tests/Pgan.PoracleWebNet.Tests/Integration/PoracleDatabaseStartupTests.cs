using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Pgan.PoracleWebNet.Tests.Integration;

/// <summary>
/// PoracleWeb.NET starts without any Poracle database setting, and an install that still carries the old
/// <c>DB_*</c> variables starts too, with one line saying they are dead.
/// </summary>
/// <remarks>
/// Environment variables are process-wide, so these two run in their own collection and restore what they
/// found.
/// </remarks>
[Collection("PoracleDbEnvironment")]
public sealed class PoracleDatabaseStartupTests
{
    private const string DeadDb = "Server=127.0.0.1;Port=1;Database=none;User=none;Password=none";
    private const string Line = "DB_* settings are no longer used by PoracleWeb.NET and can be removed.";
    private static readonly string[] LegacyVariables =
    [
        "DB_HOST", "DB_PORT", "DB_NAME", "DB_USER", "DB_PASSWORD", "ConnectionStrings__PoracleDb",
        "WEB_DB_HOST", "WEB_DB_PORT", "WEB_DB_NAME", "WEB_DB_USER", "WEB_DB_PASSWORD", "ConnectionStrings__PoracleWebDb",
    ];

    [Fact]
    public async Task StartsWithoutAPoracleConnectionString()
    {
        using var env = new LegacyEnvironment(null);
        var logs = new CapturedLogs();
        await using var factory = new Factory(logs);

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/version");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(logs.Messages, m => m == Line);
    }

    [Fact]
    public async Task RefusesToStartWithoutThePoracleWebConnectionString()
    {
        using var env = new LegacyEnvironment(null);
        var logs = new CapturedLogs();
        await using var factory = new Factory(logs, null);

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        var message = (ex as InvalidOperationException ?? ex.InnerException as InvalidOperationException)?.Message ?? ex.ToString();
        Assert.Contains("Configuration 'ConnectionStrings:PoracleWebDb' is required but was not provided.", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LogsOnceWhenLegacyDbSettingsArePresent()
    {
        using var env = new LegacyEnvironment("legacy-host");
        var logs = new CapturedLogs();
        await using var factory = new Factory(logs);

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/version");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(logs.Messages, m => m == Line);
    }

    private sealed class LegacyEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> _saved = LegacyVariables.ToDictionary(v => v, Environment.GetEnvironmentVariable);

        public LegacyEnvironment(string? host)
        {
            foreach (var variable in LegacyVariables)
            {
                Environment.SetEnvironmentVariable(variable, null);
            }

            Environment.SetEnvironmentVariable("DB_HOST", host);
        }

        public void Dispose()
        {
            foreach (var (name, value) in this._saved)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Capture(this.Messages);

        public void Dispose()
        {
        }

        private sealed class Capture(ConcurrentQueue<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Information)
                {
                    sink.Enqueue(formatter(state, exception));
                }
            }
        }
    }

    private sealed class Factory(CapturedLogs logs, string? webDb = DeadDb) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.UseEnvironment(Environments.Production);
            if (webDb is not null)
            {
                builder.UseSetting("ConnectionStrings:PoracleWebDb", webDb);
            }

            builder.UseSetting("Jwt:Secret", "integration-test-jwt-secret-at-least-32-chars");
            builder.UseSetting("Jwt:Issuer", "PoracleWeb");
            builder.UseSetting("Jwt:Audience", "PoracleWeb.App");
            builder.UseSetting("Discord:ClientId", "test-client-id");
            builder.UseSetting("Discord:ClientSecret", "test-client-secret");
            builder.UseSetting("Cors:AllowedOrigins:0", "https://allowed.example");
            builder.UseSetting("Poracle:ApiAddress", "http://127.0.0.1:1");
            builder.UseSetting("Poracle:ApiSecret", "secret");

            builder.ConfigureLogging(l => l.AddProvider(logs));
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
