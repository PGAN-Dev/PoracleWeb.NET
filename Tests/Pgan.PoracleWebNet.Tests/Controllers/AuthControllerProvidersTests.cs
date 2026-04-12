using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Tests.Controllers;

/// <summary>
/// Tests for AuthController.Providers() — the unauthenticated login provider availability endpoint.
/// </summary>
public class AuthControllerProvidersTests : ControllerTestBase
{
    private readonly Mock<ISiteSettingService> _siteSettingService = new();
    private readonly IConfiguration _config = new ConfigurationBuilder().Build();

    private AuthController CreateController(DiscordSettings? discord = null, TelegramSettings? telegram = null) => new AuthController(
            new Mock<IHumanService>().Object,
            new Mock<IPoracleApiProxy>().Object,
            new Mock<IPoracleHumanProxy>().Object,
            this._siteSettingService.Object,
            new Mock<IWebhookDelegateService>().Object,
            new Mock<IJwtService>().Object,
            Options.Create(discord ?? new DiscordSettings { ClientId = "test-id", ClientSecret = "test-secret" }),
            Options.Create(telegram ?? new TelegramSettings()),
            Options.Create(new PoracleSettings()),
            this._config,
            new Mock<ILogger<AuthController>>().Object);

    [Fact]
    public async Task ProvidersDiscordConfiguredWhenClientIdAndSecretPresent()
    {
        var controller = this.CreateController();

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("discord").GetProperty("configured").GetBoolean());
    }

    [Fact]
    public async Task ProvidersDiscordNotConfiguredWhenClientIdMissing()
    {
        var controller = this.CreateController(discord: new DiscordSettings { ClientId = "", ClientSecret = "secret" });

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("discord").GetProperty("configured").GetBoolean());
    }

    [Fact]
    public async Task ProvidersDiscordEnabledByAdminWhenSettingAbsent()
    {
        this._siteSettingService.Setup(s => s.GetValueAsync("enable_discord")).ReturnsAsync((string?)null);
        var controller = this.CreateController();

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("discord").GetProperty("enabledByAdmin").GetBoolean());
    }

    [Fact]
    public async Task ProvidersDiscordDisabledByAdminWhenSettingFalse()
    {
        this._siteSettingService.Setup(s => s.GetValueAsync("enable_discord")).ReturnsAsync("false");
        var controller = this.CreateController();

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("discord").GetProperty("enabledByAdmin").GetBoolean());
    }

    [Fact]
    public async Task ProvidersTelegramConfiguredWhenEnabled()
    {
        var controller = this.CreateController(telegram: new TelegramSettings { Enabled = true, BotUsername = "testbot" });

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        var telegram = doc.RootElement.GetProperty("telegram");
        Assert.True(telegram.GetProperty("configured").GetBoolean());
        Assert.Equal("testbot", telegram.GetProperty("botUsername").GetString());
    }

    [Fact]
    public async Task ProvidersTelegramNotConfiguredWhenDisabledInEnv()
    {
        var controller = this.CreateController(telegram: new TelegramSettings { Enabled = false });

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("telegram").GetProperty("configured").GetBoolean());
    }

    [Fact]
    public async Task ProvidersTelegramDisabledByAdminWhenSettingFalse()
    {
        this._siteSettingService.Setup(s => s.GetValueAsync("enable_telegram")).ReturnsAsync("false");
        var controller = this.CreateController(telegram: new TelegramSettings { Enabled = true, BotUsername = "bot" });

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        var telegram = doc.RootElement.GetProperty("telegram");
        Assert.True(telegram.GetProperty("configured").GetBoolean());
        Assert.False(telegram.GetProperty("enabledByAdmin").GetBoolean());
    }

    [Fact]
    public async Task ProvidersTelegramEnabledByAdminWhenSettingAbsent()
    {
        this._siteSettingService.Setup(s => s.GetValueAsync("enable_telegram")).ReturnsAsync((string?)null);
        var controller = this.CreateController(telegram: new TelegramSettings { Enabled = true, BotUsername = "bot" });

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("telegram").GetProperty("enabledByAdmin").GetBoolean());
    }

    [Fact]
    public async Task ProvidersFirstTimeSetupEmptyDbBothDefaultEnabled()
    {
        // Simulate first-time setup: no rows in site_settings
        this._siteSettingService.Setup(s => s.GetValueAsync(It.IsAny<string>())).ReturnsAsync((string?)null);
        var controller = this.CreateController(
            discord: new DiscordSettings { ClientId = "id", ClientSecret = "secret" },
            telegram: new TelegramSettings { Enabled = true, BotUsername = "bot" });

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);

        // Both should be configured and enabled by admin (absent = enabled)
        Assert.True(doc.RootElement.GetProperty("discord").GetProperty("configured").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("discord").GetProperty("enabledByAdmin").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("telegram").GetProperty("configured").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("telegram").GetProperty("enabledByAdmin").GetBoolean());
    }

    [Fact]
    public async Task ProvidersTelegramBotUsernameEmptyWhenNotConfigured()
    {
        var controller = this.CreateController(telegram: new TelegramSettings { Enabled = false, BotUsername = "secretbot" });

        var result = await controller.Providers();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        var doc = JsonDocument.Parse(json);
        Assert.Equal(string.Empty, doc.RootElement.GetProperty("telegram").GetProperty("botUsername").GetString());
    }
}
