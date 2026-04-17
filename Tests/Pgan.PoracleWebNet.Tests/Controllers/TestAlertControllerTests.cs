using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class TestAlertControllerTests : ControllerTestBase
{
    private readonly Mock<ITestAlertService> _service = new();
    private readonly Mock<IFeatureGate> _featureGate = new();
    private readonly Mock<ILogger<TestAlertController>> _logger = new();
    private readonly TestAlertController _sut;

    public TestAlertControllerTests()
    {
        // Default: no feature is disabled. Individual tests can override.
        this._featureGate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._sut = new TestAlertController(this._service.Object, this._featureGate.Object, this._logger.Object);
        SetupUser(this._sut);
    }

    [Theory]
    [InlineData("pokemon")]
    [InlineData("raid")]
    [InlineData("egg")]
    [InlineData("quest")]
    [InlineData("invasion")]
    [InlineData("lure")]
    [InlineData("nest")]
    [InlineData("gym")]
    public async Task SendTestAlertValidTypeReturnsOk(string type)
    {
        this._service.Setup(s => s.SendTestAlertAsync("123456789", type, 42)).Returns(Task.CompletedTask);

        var result = await this._sut.SendTestAlert(type, 42);

        Assert.IsType<OkObjectResult>(result);
        this._service.Verify(s => s.SendTestAlertAsync("123456789", type, 42), Times.Once);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("monsters")]
    public async Task SendTestAlertInvalidTypeReturnsBadRequest(string type)
    {
        var result = await this._sut.SendTestAlert(type, 1);

        Assert.IsType<BadRequestObjectResult>(result);
        this._service.Verify(s => s.SendTestAlertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task SendTestAlertNotSupportedReturns501()
    {
        // Nest alarms surface as NotSupportedException from the service — the controller
        // must translate that into HTTP 501 so the frontend can render a clear message.
        this._service.Setup(s => s.SendTestAlertAsync("123456789", "nest", 7))
            .ThrowsAsync(new NotSupportedException("Nest test alerts aren't supported by PoracleNG's /api/test endpoint."));

        var result = await this._sut.SendTestAlert("nest", 7);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(501, status.StatusCode);
    }

    [Fact]
    public async Task SendTestAlertAlarmNotFoundReturnsNotFound()
    {
        this._service.Setup(s => s.SendTestAlertAsync("123456789", "pokemon", 999))
            .ThrowsAsync(new KeyNotFoundException("Alarm with uid 999 not found"));

        var result = await this._sut.SendTestAlert("pokemon", 999);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task SendTestAlertServiceErrorReturns500()
    {
        this._service.Setup(s => s.SendTestAlertAsync("123456789", "pokemon", 1))
            .ThrowsAsync(new InvalidOperationException("Something went wrong"));

        var result = await this._sut.SendTestAlert("pokemon", 1);

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    [Theory]
    [InlineData("pokemon", DisableFeatureKeys.Pokemon)]
    [InlineData("raid", DisableFeatureKeys.Raids)]
    [InlineData("egg", DisableFeatureKeys.Raids)]
    [InlineData("quest", DisableFeatureKeys.Quests)]
    [InlineData("invasion", DisableFeatureKeys.Invasions)]
    [InlineData("lure", DisableFeatureKeys.Lures)]
    [InlineData("nest", DisableFeatureKeys.Nests)]
    [InlineData("gym", DisableFeatureKeys.Gyms)]
    public async Task SendTestAlertThrowsFeatureDisabledExceptionWhenFeatureDisabled(string type, string disableKey)
    {
        // #236: when an admin has disabled a type, non-admin users must not be able to fire test alerts for it.
        // The exception is mapped to HTTP 403 by the global FeatureDisabledExceptionFilter.
        this._featureGate
            .Setup(g => g.EnsureEnabledAsync(disableKey))
            .ThrowsAsync(new FeatureDisabledException(disableKey));

        var ex = await Assert.ThrowsAsync<FeatureDisabledException>(() => this._sut.SendTestAlert(type, 1));

        Assert.Equal(disableKey, ex.DisableKey);
        this._service.Verify(s => s.SendTestAlertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task SendTestAlertAdminAlsoBlockedByDisabledFeature()
    {
        // Admins are not exempt — the toggle means "nobody fires this alarm type." See #236.
        this._featureGate
            .Setup(g => g.EnsureEnabledAsync(DisableFeatureKeys.Pokemon))
            .ThrowsAsync(new FeatureDisabledException(DisableFeatureKeys.Pokemon));
        SetupUser(this._sut, isAdmin: true);

        await Assert.ThrowsAsync<FeatureDisabledException>(() => this._sut.SendTestAlert("pokemon", 1));

        this._service.Verify(s => s.SendTestAlertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }
}
