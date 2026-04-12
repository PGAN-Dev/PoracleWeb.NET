using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class SettingsMigrationServiceTests
{
    private static readonly System.Text.Json.JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    private readonly Mock<IPwebSettingService> _pwebSettingService = new();
    private readonly Mock<ISiteSettingService> _siteSettingService = new();
    private readonly Mock<IWebhookDelegateService> _webhookDelegateService = new();
    private readonly Mock<IQuickPickDefinitionRepository> _quickPickDefinitionRepo = new();
    private readonly Mock<IQuickPickAppliedStateRepository> _quickPickAppliedStateRepo = new();
    private readonly Mock<ILogger<SettingsMigrationService>> _logger = new();
    private readonly SettingsMigrationService _sut;

    public SettingsMigrationServiceTests() => this._sut = new SettingsMigrationService(
        this._pwebSettingService.Object,
        this._siteSettingService.Object,
        this._webhookDelegateService.Object,
        this._quickPickDefinitionRepo.Object,
        this._quickPickAppliedStateRepo.Object,
        this._logger.Object);

    [Fact]
    public async Task MigrateAsyncSkipsWhenAlreadyMigrated()
    {
        this._siteSettingService.Setup(s => s.GetByKeyAsync("migration_completed"))
            .ReturnsAsync(new SiteSetting { Key = "migration_completed", Value = "true" });

        await this._sut.MigrateAsync();

        this._pwebSettingService.Verify(s => s.GetAllAsync(), Times.Never);
    }

    [Fact]
    public async Task MigrateAsyncMigratesSiteSettings()
    {
        this.SetupNotMigrated();
        this._pwebSettingService.Setup(s => s.GetAllAsync()).ReturnsAsync(
        [
            new() { Setting = "custom_title", Value = "My App" },
            new() { Setting = "debug", Value = "false" }
        ]);
        this._siteSettingService.Setup(s => s.CreateOrUpdateAsync(It.IsAny<SiteSetting>()))
            .ReturnsAsync((SiteSetting s) => s);

        await this._sut.MigrateAsync();

        this._siteSettingService.Verify(s => s.CreateOrUpdateAsync(It.Is<SiteSetting>(ss =>
            ss.Key == "custom_title" && ss.Value == "My App")), Times.Once);
    }

    [Fact]
    public async Task MigrateAsyncMigratesWebhookDelegates()
    {
        this.SetupNotMigrated();
        this._pwebSettingService.Setup(s => s.GetAllAsync()).ReturnsAsync(
        [
            new() { Setting = "webhook_delegates:wh1", Value = "u1,u2" },
            new() { Setting = "webhook_delegates:wh2", Value = "u3" }
        ]);
        this._siteSettingService.Setup(s => s.CreateOrUpdateAsync(It.IsAny<SiteSetting>()))
            .ReturnsAsync((SiteSetting s) => s);
        this._webhookDelegateService.Setup(s => s.AddDelegateAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync([]);

        await this._sut.MigrateAsync();

        this._webhookDelegateService.Verify(s => s.AddDelegateAsync("wh1", "u1"), Times.Once);
        this._webhookDelegateService.Verify(s => s.AddDelegateAsync("wh1", "u2"), Times.Once);
        this._webhookDelegateService.Verify(s => s.AddDelegateAsync("wh2", "u3"), Times.Once);
    }

    [Fact]
    public async Task MigrateAsyncMigratesQuickPickDefinitions()
    {
        this.SetupNotMigrated();
        var definition = new QuickPickDefinition { Id = "hundo", Name = "100% IV", AlarmType = "monster" };
        var json = System.Text.Json.JsonSerializer.Serialize(definition, CamelCaseOptions);
        this._pwebSettingService.Setup(s => s.GetAllAsync()).ReturnsAsync(
        [
            new() { Setting = "quick_pick:hundo", Value = json }
        ]);
        this._siteSettingService.Setup(s => s.CreateOrUpdateAsync(It.IsAny<SiteSetting>()))
            .ReturnsAsync((SiteSetting s) => s);

        await this._sut.MigrateAsync();

        this._quickPickDefinitionRepo.Verify(r => r.CreateOrUpdateAsync(It.Is<QuickPickDefinition>(d =>
            d.Id == "hundo" && d.Scope == "global")), Times.Once);
    }

    [Fact]
    public async Task MigrateAsyncSetsMigrationCompletedSentinel()
    {
        this.SetupNotMigrated();
        this._pwebSettingService.Setup(s => s.GetAllAsync()).ReturnsAsync([]);
        this._siteSettingService.Setup(s => s.CreateOrUpdateAsync(It.IsAny<SiteSetting>()))
            .ReturnsAsync((SiteSetting s) => s);

        await this._sut.MigrateAsync();

        this._siteSettingService.Verify(s => s.CreateOrUpdateAsync(It.Is<SiteSetting>(ss =>
            ss.Key == "migration_completed" && ss.Value != null)), Times.Once);
    }

    [Fact]
    public async Task MigrateAsyncContinuesOnIndividualFailure()
    {
        this.SetupNotMigrated();
        this._pwebSettingService.Setup(s => s.GetAllAsync()).ReturnsAsync(
        [
            new() { Setting = "failing_key", Value = "val1" },
            new() { Setting = "succeeding_key", Value = "val2" }
        ]);

        this._siteSettingService.Setup(s => s.CreateOrUpdateAsync(It.IsAny<SiteSetting>()))
            .ReturnsAsync((SiteSetting s) =>
            {
                if (s.Key == "failing_key")
                {
                    throw new InvalidOperationException("DB error");
                }

                return s;
            });

        await this._sut.MigrateAsync();

        this._siteSettingService.Verify(s => s.CreateOrUpdateAsync(It.Is<SiteSetting>(ss =>
            ss.Key == "succeeding_key")), Times.Once);
    }

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

    private void SetupNotMigrated() => this._siteSettingService.Setup(s => s.GetByKeyAsync("migration_completed"))
            .ReturnsAsync((SiteSetting?)null);
}
