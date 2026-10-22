using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Api.Services;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class SettingsMigrationServiceTests
{
    private readonly Mock<ISiteSettingService> _siteSettingService = new();
    private readonly Mock<IQuickPickDefinitionRepository> _quickPickDefinitionRepo = new();
    private readonly Mock<ILogger<SettingsMigrationService>> _logger = new();
    private readonly SettingsMigrationService _sut;

    public SettingsMigrationServiceTests() => this._sut = new SettingsMigrationService(
        this._siteSettingService.Object,
        this._quickPickDefinitionRepo.Object,
        this._logger.Object);

    // ── SeedDefaultsAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task SeedDefaultsAsyncSeedsCustomTitleWhenMissing()
    {
        this._siteSettingService.Setup(s => s.GetByKeyAsync("custom_title"))
            .ReturnsAsync((SiteSetting?)null);
        this._siteSettingService.Setup(s => s.CreateOrUpdateAsync(It.IsAny<SiteSetting>()))
            .ReturnsAsync((SiteSetting s) => s);

        await this._sut.SeedDefaultsAsync();

        this._siteSettingService.Verify(s => s.CreateOrUpdateAsync(It.Is<SiteSetting>(ss =>
            ss.Key == "custom_title" && ss.Value == "PoracleWeb.NET")), Times.Once);
    }

    [Fact]
    public async Task SeedDefaultsAsyncDoesNotOverwriteExistingCustomTitle()
    {
        this._siteSettingService.Setup(s => s.GetByKeyAsync("custom_title"))
            .ReturnsAsync(new SiteSetting { Key = "custom_title", Value = "My Custom Title" });

        await this._sut.SeedDefaultsAsync();

        this._siteSettingService.Verify(s => s.CreateOrUpdateAsync(It.IsAny<SiteSetting>()), Times.Never);
    }

    [Fact]
    public async Task StartupRunsSeedingWithoutAPoracleDatabase()
    {
        var seeding = new Mock<ISettingsMigrationService>();
        var seeded = new TaskCompletionSource();
        seeding.Setup(s => s.SeedDefaultsAsync()).Callback(() => seeded.TrySetResult()).Returns(Task.CompletedTask);

        var provider = new ServiceCollection()
            .AddSingleton(seeding.Object)
            .BuildServiceProvider();
        var sut = new SettingsMigrationStartupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new Mock<ILogger<SettingsMigrationStartupService>>().Object);

        await sut.StartAsync(CancellationToken.None);
        await seeded.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await sut.StopAsync(CancellationToken.None);

        seeding.Verify(s => s.SeedDefaultsAsync(), Times.Once);
        seeding.VerifyNoOtherCalls();
    }
}
