using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class CrossProfileControllerTests : ControllerTestBase
{
    private readonly Mock<IPoracleHumanProxy> _humanProxy = new();
    private readonly Mock<IProfileService> _profileService = new();
    private readonly Mock<ICrossProfileService> _service = new();
    private readonly CrossProfileController _sut;

    public CrossProfileControllerTests()
    {
        var jwtSettings = Options.Create(new JwtSettings
        {
            Audience = "test",
            ExpirationMinutes = 60,
            Issuer = "test",
            Secret = "test-secret-key-that-is-at-least-32-characters-long",
        });
        this._sut = new CrossProfileController(
            this._service.Object,
            this._profileService.Object,
            this._humanProxy.Object,
            jwtSettings);
        SetupUser(this._sut);
    }

    [Fact]
    public async Task GetAllProfilesOverviewCallsServiceWithUserId()
    {
        var json = CreateJsonObject(new
        {
        });
        this._service.Setup(s => s.GetAllProfilesOverviewAsync("123456789")).ReturnsAsync(json);

        await this._sut.GetAllProfilesOverview();

        this._service.Verify(s => s.GetAllProfilesOverviewAsync("123456789"), Times.Once);
    }

    [Fact]
    public async Task GetAllProfilesOverviewReturnsOkWithData()
    {
        var json = CreateJsonObject(new
        {
            pokemon = new[] { new { uid = 1 } }
        });
        this._service.Setup(s => s.GetAllProfilesOverviewAsync("123456789")).ReturnsAsync(json);

        var result = await this._sut.GetAllProfilesOverview();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(json.ToString(), ok.Value?.ToString());
    }

    private static JsonElement CreateJsonObject(object obj)
    {
        var json = JsonSerializer.Serialize(obj);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
