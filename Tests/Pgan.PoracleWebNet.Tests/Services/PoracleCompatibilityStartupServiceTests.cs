using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Api.Services;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Services;

public class PoracleCompatibilityStartupServiceTests
{
    private sealed class CapturingLogger : ILogger<PoracleCompatibilityStartupService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            this.Entries.Add((logLevel, formatter(state, exception)));
    }

    private static async Task<CapturingLogger> RunAsync(string version)
    {
        var profiles = new Mock<IPoracleServerProfileService>();
        profiles.Setup(p => p.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PoracleServerProfile { Version = version, Reachable = true, CheckedAt = DateTimeOffset.UtcNow });
        var provider = new ServiceCollection().AddSingleton(profiles.Object).BuildServiceProvider();
        var logger = new CapturingLogger();
        var service = new PoracleCompatibilityStartupService(provider.GetRequiredService<IServiceScopeFactory>(), logger)
        {
            StartupDelay = TimeSpan.Zero,
        };

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(15));
        await service.StopAsync(CancellationToken.None);
        return logger;
    }

    [Theory]
    [InlineData("5.3.0")]
    [InlineData("5.2.1")]
    public async Task ATooOldServerIsToldThatFiveFourZeroIsRequired(string version)
    {
        var logger = await RunAsync(version);

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("5.4.0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AServerAtTheMinimumIsNotComplainedAbout()
    {
        var logger = await RunAsync("5.4.0");

        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("5.4.0", StringComparison.Ordinal));
    }
}
