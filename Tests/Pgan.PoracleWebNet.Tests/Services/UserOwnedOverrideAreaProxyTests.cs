using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Confining an alarm to a geofence the user drew themselves.
/// <para>
/// PoracleNG validates <c>override_areas</c> against <c>GetAvailableAreas</c>, which filters on
/// <c>userSelectable</c>; PoracleWeb serves user-drawn fences with <c>userSelectable: false</c>, so
/// submitting one is refused with 400 and the whole write fails. Matching never consults that flag, so
/// the name is sent separately, straight into the column, and matches normally.
/// </para>
/// </summary>
public class UserOwnedOverrideAreaProxyTests
{
    private const string User = "u1";

    private readonly Mock<IPoracleTrackingProxy> _inner = new();
    private readonly Mock<IUserGeofenceRepository> _geofences = new();
    private readonly Mock<IUserAreaDualWriter> _writer = new();
    private readonly List<JsonElement> _sentToPoracle = [];

    public UserOwnedOverrideAreaProxyTests()
    {
        this._inner
            .Setup(p => p.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JsonElement>()))
            .Callback<string, string, JsonElement>((_, _, body) => this._sentToPoracle.Add(body.Clone()))
            .ReturnsAsync(new TrackingCreateResult([7], 0, 0, 1));
        this._inner.Setup(p => p.ReloadStateAsync()).Returns(Task.CompletedTask);
        this._geofences.Setup(r => r.GetByHumanIdAsync(It.IsAny<string>()))
            .ReturnsAsync([new UserGeofence { KojiName = "back garden", HumanId = User }]);
        this._writer
            .Setup(w => w.SetAlarmOverrideAreasAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(true);
    }

    private UserOwnedOverrideAreaProxy Proxy() => new(
        this._inner.Object,
        this._geofences.Object,
        this._writer.Object,
        NullLogger<UserOwnedOverrideAreaProxy>.Instance);

    private static JsonElement Row(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task OwnedGeofenceIsStrippedFromThePoracleWriteAndWrittenDirectly()
    {
        await this.Proxy().CreateAsync("pokemon", User, Row(
            """{"uid":7,"pokemon_id":201,"distance":0,"override_areas":["back garden"]}"""));

        // PoracleNG would refuse the whole request if it saw the name, so the property goes away entirely
        // rather than being sent as an empty list.
        Assert.False(Assert.Single(this._sentToPoracle).TryGetProperty("override_areas", out _));

        this._writer.Verify(
            w => w.SetAlarmOverrideAreasAsync(
                User, "pokemon", 7, It.Is<IReadOnlyCollection<string>>(a => a.Contains("back garden"))),
            Times.Once);
    }

    [Fact]
    public async Task PermittedAreasStillGoToPoracleAlongsideTheOwnedOne()
    {
        // The legitimate-case half: an admin area must keep travelling the normal path, and the column
        // write has to carry BOTH names or the alarm quietly loses the public area.
        await this.Proxy().CreateAsync("raid", User, Row(
            """{"uid":7,"level":5,"distance":0,"override_areas":["terrigal","back garden"]}"""));

        var sent = Assert.Single(this._sentToPoracle).GetProperty("override_areas");
        Assert.Equal("terrigal", Assert.Single(sent.EnumerateArray()).GetString());

        this._writer.Verify(
            w => w.SetAlarmOverrideAreasAsync(
                User, "raid", 7,
                It.Is<IReadOnlyCollection<string>>(a => a.Count == 2 && a.Contains("terrigal") && a.Contains("back garden"))),
            Times.Once);
    }

    [Fact]
    public async Task AdminOnlyAreasNeverTouchTheDatabase()
    {
        await this.Proxy().CreateAsync("pokemon", User, Row(
            """{"uid":7,"pokemon_id":201,"distance":0,"override_areas":["terrigal"]}"""));

        var sent = Assert.Single(this._sentToPoracle).GetProperty("override_areas");
        Assert.Equal("terrigal", Assert.Single(sent.EnumerateArray()).GetString());
        this._writer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AWriteWithNoOverrideCostsNoGeofenceLookup()
    {
        // Every ordinary alarm write goes through here. It must not pay for a feature it does not use.
        await this.Proxy().CreateAsync("pokemon", User, Row("""{"uid":7,"pokemon_id":201,"distance":500}"""));

        this._geofences.VerifyNoOtherCalls();
        this._writer.VerifyNoOtherCalls();
        Assert.Single(this._sentToPoracle);
    }

    [Fact]
    public async Task StateIsReloadedSoTheDirectWriteTakesEffect()
    {
        await this.Proxy().CreateAsync("pokemon", User, Row(
            """{"uid":7,"pokemon_id":201,"distance":0,"override_areas":["back garden"]}"""));

        this._inner.Verify(p => p.ReloadStateAsync(), Times.Once);
    }

    [Fact]
    public async Task AMissingRowAfterTheWriteIsAnErrorRatherThanASilentlyWiderAlarm()
    {
        this._writer
            .Setup(w => w.SetAlarmOverrideAreasAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => this.Proxy().CreateAsync("pokemon", User, Row(
            """{"uid":7,"pokemon_id":201,"distance":0,"override_areas":["back garden"]}""")));
    }

    [Theory]
    [InlineData("""{"override_location_label":"home","override_areas":["terrigal"],"distance":500}""",
        "place or to areas")]
    [InlineData("""{"override_areas":["terrigal"],"distance":500}""", "cannot also have a radius")]
    [InlineData("""{"override_location_label":"home","distance":0}""", "needs a radius")]
    public async Task AnIncoherentScopeIsRefusedBeforeAnythingIsWritten(string body, string expected)
    {
        var error = await Assert.ThrowsAsync<AlarmValidationException>(
            () => this.Proxy().CreateAsync("pokemon", User, Row(body)));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        Assert.Empty(this._sentToPoracle);
    }

    [Theory]
    [InlineData("""{"override_location_label":"home","distance":500}""")]
    [InlineData("""{"override_areas":["terrigal"],"distance":0}""")]
    [InlineData("""{"distance":500}""")]
    public async Task ACoherentScopeIsLetThrough(string body)
    {
        // Each of the three refusals above needs its legitimate twin, or the guard is free to refuse
        // everything and still pass its tests.
        await this.Proxy().CreateAsync("pokemon", User, Row(body));

        Assert.Single(this._sentToPoracle);
    }
}
