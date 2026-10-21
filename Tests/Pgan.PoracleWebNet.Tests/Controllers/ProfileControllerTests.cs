using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Pgan.PoracleWebNet.Api.Configuration;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

public class ProfileControllerTests : ControllerTestBase
{
    private readonly Mock<IProfileService> _profileService = new();
    private readonly Mock<IHumanService> _humanService = new();
    private readonly Mock<IPoracleHumanProxy> _humanProxy = new();
    private readonly ProfileController _sut;

    private readonly Mock<IJwtService> _jwtService = new();
    private readonly Mock<Pgan.PoracleWebNet.Api.Services.IUserRoleResolver> _roleResolver = new();
    private readonly Mock<IUserGeofenceRepository> _userGeofenceRepository = new();

    public ProfileControllerTests()
    {
        this._jwtService.Setup(j => j.GenerateTokenWithReplacedProfile(It.IsAny<System.Security.Claims.ClaimsPrincipal>(), It.IsAny<int>(), It.IsAny<bool?>()))
            .Returns("test-jwt-token");

        this._sut = new ProfileController(this._profileService.Object, this._humanService.Object, this._humanProxy.Object, this._jwtService.Object, this._roleResolver.Object, this._userGeofenceRepository.Object, Microsoft.Extensions.Logging.Abstractions.NullLogger<ProfileController>.Instance);
        SetupUser(this._sut);
    }

    /// <summary>
    /// PoracleNG picks the profile number and reports it from the create, so the controller reads no
    /// profile list to find out which one it got. See #407.
    /// </summary>
    private void SetupCreateAssigns(int newProfileNo, string name)
    {
        this._humanProxy.Setup(h => h.AddProfileAsync("123456789", It.IsAny<JsonElement>())).ReturnsAsync(newProfileNo);
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", newProfileNo))
            .ReturnsAsync(new Profile { Id = "123456789", ProfileNo = newProfileNo, Name = name });
    }

    private static Profile P(int no, string name) => new() { Id = "123456789", ProfileNo = no, Name = name };

    [Fact]
    public async Task GetAllReturnsOkWithProfiles()
    {
        this._profileService.Setup(s => s.GetByUserAsync("123456789")).ReturnsAsync([new() { ProfileNo = 1 }]);
        var result = await this._sut.GetAll();
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task CreateReturnsCreatedAtAction()
    {
        var profile = new Profile { Name = "New" };
        this.SetupCreateAssigns(1, "New");
        var result = await this._sut.Create(profile);
        Assert.IsType<CreatedAtActionResult>(result);
        this._humanProxy.Verify(p => p.AddProfileAsync("123456789", It.IsAny<JsonElement>()), Times.Once);
    }

    [Fact]
    public async Task CreateSetsUserId()
    {
        var profile = new Profile { Name = "New" };
        this.SetupCreateAssigns(1, "New");
        await this._sut.Create(profile);
        Assert.Equal("123456789", profile.Id);
    }

    [Fact]
    public async Task UpdateReturnsOkWhenFound()
    {
        var existing = new Profile { Id = "123456789", ProfileNo = 1, Name = "Old" };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(existing);

        var result = await this._sut.Update(1, new Profile { Name = "Updated" });

        Assert.IsType<OkObjectResult>(result);
        this._humanProxy.Verify(p => p.UpdateProfileAsync("123456789", It.IsAny<JsonElement>()), Times.Once);
    }

    [Fact]
    public async Task UpdateReturnsNotFoundWhenMissing()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 99)).ReturnsAsync((Profile?)null);
        Assert.IsType<NotFoundResult>(await this._sut.Update(99, new Profile()));
    }

    [Fact]
    public async Task SwitchProfileReturnsOkAndCallsProxy()
    {
        var profile = new Profile { Id = "123456789", ProfileNo = 2, Area = "[\"new area\"]" };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 2)).ReturnsAsync(profile);

        var result = await this._sut.SwitchProfile(2);

        Assert.IsType<OkObjectResult>(result);
        this._humanProxy.Verify(p => p.SwitchProfileAsync("123456789", 2), Times.Once);
    }

    [Fact]
    public async Task SwitchProfileReturnsNotFoundWhenProfileMissing()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 99)).ReturnsAsync((Profile?)null);
        Assert.IsType<NotFoundResult>(await this._sut.SwitchProfile(99));
    }

    [Fact]
    public async Task DuplicateCreatesProfileAndCopiesAlarms()
    {
        var sourceProfile = new Profile { Id = "123456789", ProfileNo = 1, Name = "Main", Area = "[\"area1\"]" };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(sourceProfile);
        this.SetupCreateAssigns(2, "Main (copy)");

        var result = await this._sut.Duplicate(new DuplicateProfileRequest { FromProfileNo = 1, Name = "Main (copy)" });

        Assert.IsType<CreatedAtActionResult>(result);
        this._humanProxy.Verify(p => p.AddProfileAsync("123456789", It.IsAny<JsonElement>()), Times.Once);
        this._profileService.Verify(s => s.CopyAsync("123456789", 1, 2), Times.Once);
    }

    [Fact]
    public async Task DuplicateCopiesActiveHoursFromSource()
    {
        var schedule = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"09\",\"mins\":\"00\"}]";
        var sourceProfile = new Profile { Id = "123456789", ProfileNo = 1, Name = "Main", ActiveHours = schedule };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(sourceProfile);
        this.SetupCreateAssigns(2, "Copy");

        JsonElement? capturedBody = null;
        this._humanProxy
            .Setup(p => p.AddProfileAsync("123456789", It.IsAny<JsonElement>()))
            .Callback<string, JsonElement>((_, body) => capturedBody = body.Clone())
            .ReturnsAsync(2);

        await this._sut.Duplicate(new DuplicateProfileRequest { FromProfileNo = 1, Name = "Copy" });

        Assert.NotNull(capturedBody);
        Assert.True(capturedBody.Value.TryGetProperty("active_hours", out var ah));
        Assert.Equal(schedule, ah.GetString());
    }

    [Fact]
    public async Task DuplicateReturnsNotFoundWhenSourceMissing()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 99)).ReturnsAsync((Profile?)null);
        Assert.IsType<NotFoundResult>(await this._sut.Duplicate(new DuplicateProfileRequest { FromProfileNo = 99, Name = "Copy" }));
    }

    [Fact]
    public async Task DeleteReturnsNoContentAndCallsProxy()
    {
        var existing = new Profile { Id = "123456789", ProfileNo = 2 };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 2)).ReturnsAsync(existing);

        Assert.IsType<NoContentResult>(await this._sut.Delete(2));
        this._humanProxy.Verify(p => p.DeleteProfileAsync("123456789", 2), Times.Once);
    }

    [Fact]
    public async Task DeleteReturnsNotFoundWhenProfileMissing()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 99)).ReturnsAsync((Profile?)null);
        Assert.IsType<NotFoundResult>(await this._sut.Delete(99));
    }

    // ---- profile numbering (#407): the create reports the number, nothing is diffed ---------------

    [Fact]
    public async Task CreateUsesTheNumberPoracleAssignedAndReadsNoProfileList()
    {
        // PoracleNG assigns the lowest free number. Profiles 0, 1, 3 -> 2, and it says so; arithmetic
        // (max + 1 = 4) and a before/after list diff are both gone.
        this.SetupCreateAssigns(2, "Gap Filler");

        var result = await this._sut.Create(new Profile { Name = "Gap Filler" });

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(2, Assert.IsType<Profile>(created.Value).ProfileNo);
        this._profileService.Verify(s => s.GetByUserAsync(It.IsAny<string>()), Times.Never);
        this._profileService.Verify(s => s.GetByUserAndProfileNoAsync("123456789", 4), Times.Never);
    }

    [Fact]
    public async Task DuplicateCopiesAlarmsToTheNumberPoracleReported()
    {
        // The orphan case: alarms used to be copied to max+1 = 4 while the profile was created at 2.
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 0)).ReturnsAsync(P(0, "Source"));
        this.SetupCreateAssigns(2, "Dup");

        await this._sut.Duplicate(new DuplicateProfileRequest { FromProfileNo = 0, Name = "Dup" });

        this._profileService.Verify(s => s.CopyAsync("123456789", 0, 2), Times.Once);
        this._profileService.Verify(s => s.CopyAsync("123456789", 0, 4), Times.Never);
        this._profileService.Verify(s => s.GetByUserAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateDoesNotDictateTheProfileNumberToPoracleNg()
    {
        this.SetupCreateAssigns(1, "New");
        JsonElement? sent = null;
        this._humanProxy.Setup(h => h.AddProfileAsync("123456789", It.IsAny<JsonElement>()))
            .Callback<string, JsonElement>((_, b) => sent = b.Clone())
            .ReturnsAsync(1);

        await this._sut.Create(new Profile { Name = "New" });

        Assert.NotNull(sent);
        Assert.False(sent!.Value.TryGetProperty("profileNo", out _));
        Assert.False(sent.Value.TryGetProperty("profile_no", out _));
    }

    [Fact]
    public async Task ACreateThatReportsNoNumberIsNotAnsweredWithA201()
    {
        // The proxy throws when PoracleNG's answer carries no number; the controller must not swallow it.
        this._humanProxy.Setup(h => h.AddProfileAsync("123456789", It.IsAny<JsonElement>()))
            .ThrowsAsync(new PoracleRequestRefusedException("no profile number", 502));

        await Assert.ThrowsAsync<PoracleRequestRefusedException>(() => this._sut.Create(new Profile { Name = "Nope" }));
        this._humanProxy.Verify(
            h => h.SetProfileGeographyAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<double>(), It.IsAny<double>()),
            Times.Never);
    }

    // ---- active hours reach the proxy --------------------------------------------------------------

    [Fact]
    public async Task UpdateIncludesActiveHoursInProxyPayload()
    {
        var existing = new Profile { Id = "123456789", ProfileNo = 1, Name = "Old" };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(existing);

        JsonElement? capturedBody = null;
        this._humanProxy.Setup(p => p.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<JsonElement>()))
            .Callback<string, JsonElement>((_, body) => capturedBody = body.Clone());

        var profile = new Profile { Name = "Updated", ActiveHours = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"09\",\"mins\":\"00\"}]" };
        await this._sut.Update(1, profile);

        Assert.NotNull(capturedBody);
        Assert.True(capturedBody.Value.TryGetProperty("active_hours", out var ah));
        Assert.Equal(/*lang=json,strict*/ "[{\"day\":1,\"hours\":\"09\",\"mins\":\"00\"}]", ah.GetString());
    }

    [Fact]
    public async Task UpdateSendsNullActiveHoursWhenNotProvided()
    {
        var existing = new Profile { Id = "123456789", ProfileNo = 1, Name = "Old" };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(existing);

        JsonElement? capturedBody = null;
        this._humanProxy.Setup(p => p.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<JsonElement>()))
            .Callback<string, JsonElement>((_, body) => capturedBody = body.Clone());

        var profile = new Profile { Name = "Updated", ActiveHours = null };
        await this._sut.Update(1, profile);

        Assert.NotNull(capturedBody);
        Assert.True(capturedBody.Value.TryGetProperty("active_hours", out var ah));
        Assert.Equal(JsonValueKind.Null, ah.ValueKind);
    }

    [Fact]
    public async Task CreateIncludesActiveHoursInProxyPayload()
    {
        var activeHours = /*lang=json,strict*/ "[{\"day\":2,\"hours\":\"18\",\"mins\":\"30\"}]";
        var profile = new Profile { Name = "New", ActiveHours = activeHours };
        this.SetupCreateAssigns(1, "New");

        JsonElement? capturedBody = null;
        this._humanProxy.Setup(p => p.AddProfileAsync(It.IsAny<string>(), It.IsAny<JsonElement>()))
            .Callback<string, JsonElement>((_, body) => capturedBody = body.Clone())
            .ReturnsAsync(1);

        await this._sut.Create(profile);

        Assert.NotNull(capturedBody);
        Assert.True(capturedBody.Value.TryGetProperty("active_hours", out var ah));
        Assert.Equal(activeHours, ah.GetString());
    }

    [Fact]
    public async Task GetAllReturnsProfilesWithActiveHours()
    {
        var activeHours = /*lang=json,strict*/ "[{\"day\":1,\"hours\":\"09\",\"mins\":\"00\"}]";
        var profiles = new List<Profile>
        {
            new() { ProfileNo = 1, Name = "Default", ActiveHours = activeHours },
            new() { ProfileNo = 2, Name = "PvP", ActiveHours = null },
        };
        this._profileService.Setup(s => s.GetByUserAsync("123456789")).ReturnsAsync(profiles);
        this._humanService.Setup(s => s.GetByIdAsync("123456789")).ReturnsAsync(new Human { CurrentProfileNo = 1 });

        var result = await this._sut.GetAll();
        var okResult = Assert.IsType<OkObjectResult>(result);
        var returnedProfiles = Assert.IsType<List<Profile>>(okResult.Value, exactMatch: false);

        Assert.Equal(2, returnedProfiles.Count);
        Assert.Equal(activeHours, returnedProfiles[0].ActiveHours);
        Assert.Null(returnedProfiles[1].ActiveHours);
    }

    // ---- rename (#406, #837): always the proxy's PATCH, never a database write --------------------

    [Fact]
    public async Task ARenameIsSentToTheProxyInTheBodyAndNothingElseWritesIt()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(P(1, "Old"));
        JsonElement captured = default;
        this._humanProxy.Setup(p => p.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<JsonElement>()))
            .Callback<string, JsonElement>((_, body) => captured = body.Clone());

        Assert.IsType<OkObjectResult>(await this._sut.Update(1, new Profile { Name = "New" }));

        Assert.Equal("New", captured.GetProperty("name").GetString());
        Assert.Equal(1, captured.GetProperty("profile_no").GetInt32());
        this._humanProxy.Verify(p => p.UpdateProfileAsync("123456789", It.IsAny<JsonElement>()), Times.Once);
    }

    [Fact]
    public async Task AnUnchangedNameIsStillSentSoAnActiveHoursEditKeepsIt()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(P(1, "Keep"));
        JsonElement captured = default;
        this._humanProxy.Setup(p => p.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<JsonElement>()))
            .Callback<string, JsonElement>((_, body) => captured = body.Clone());

        await this._sut.Update(1, new Profile { ActiveHours = "[]" });

        Assert.Equal("Keep", captured.GetProperty("name").GetString());
        Assert.Equal("[]", captured.GetProperty("active_hours").GetString());
    }

    // ── Name length and duplicate geography (#466, #467) ────────────────────

    [Fact]
    public async Task CreateRejectsANameLongerThanTheColumn()
    {
        // 256 chars reached the database and came back as an opaque 500.
        var result = await this._sut.Create(new Profile { Name = new string('a', 256) });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateStillAcceptsANameAtTheLimit()
    {
        this.SetupCreateAssigns(1, new string('a', 255));

        var result = await this._sut.Create(new Profile { Name = new string('a', 255) });

        Assert.IsType<CreatedAtActionResult>(result);
    }

    [Fact]
    public async Task UpdateRejectsANameLongerThanTheColumn()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1))
            .ReturnsAsync(P(1, "Old"));

        var result = await this._sut.Update(1, new Profile { Name = new string('a', 256) });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task DuplicateCarriesTheSourceProfilesAreasAndLocation()
    {
        // PoracleNG drops area/latitude/longitude from addProfile, so the copy silently inherited the
        // ACTIVE profile's geography: the right alarms over the wrong map. See #466.
        var source = new Profile
        {
            Id = "123456789", ProfileNo = 1, Name = "Work",
            Area = "[\"downtown\",\"fan\"]", Latitude = 37.5, Longitude = -77.4,
        };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(source);
        this.SetupCreateAssigns(2, "Copy");

        await this._sut.Duplicate(new DuplicateProfileRequest { FromProfileNo = 1, Name = "Copy" });

        this._humanProxy.Verify(
            h => h.SetProfileGeographyAsync(
                "123456789", 2, It.Is<IReadOnlyCollection<string>>(a => a.SequenceEqual(new[] { "downtown", "fan" })), 37.5, -77.4),
            Times.Once);
    }

    private void GeographyFails()
    {
        this._humanProxy
            .Setup(h => h.SetProfileGeographyAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<double>(), It.IsAny<double>()))
            .ThrowsAsync(new PoracleRequestRefusedException("invalid latitude"));
    }

    [Fact]
    public async Task AGeographyFailureOnCreateDeletesTheNewProfileAndReachesTheCaller()
    {
        this.SetupCreateAssigns(1, "New");
        this.GeographyFails();

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(() => this._sut.Create(new Profile { Name = "New" }));

        Assert.Equal("invalid latitude", ex.Message);
        this._humanProxy.Verify(h => h.DeleteProfileAsync("123456789", 1), Times.Once);
    }

    [Fact]
    public async Task AGeographyFailureOnDuplicateDeletesTheNewProfileAndSkipsTheCopy()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(P(1, "Work"));
        this.SetupCreateAssigns(2, "Copy");
        this.GeographyFails();

        await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => this._sut.Duplicate(new DuplicateProfileRequest { FromProfileNo = 1, Name = "Copy" }));

        this._humanProxy.Verify(h => h.DeleteProfileAsync("123456789", 2), Times.Once);
        this._profileService.Verify(s => s.CopyAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ACopyFailureOnDuplicateStillDeletesTheNewProfile()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(P(1, "Work"));
        this.SetupCreateAssigns(2, "Copy");
        this._profileService.Setup(s => s.CopyAsync("123456789", 1, 2)).ThrowsAsync(new InvalidOperationException("copy failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => this._sut.Duplicate(new DuplicateProfileRequest { FromProfileNo = 1, Name = "Copy" }));

        this._humanProxy.Verify(h => h.DeleteProfileAsync("123456789", 2), Times.Once);
    }

    [Fact]
    public async Task AFailedRollbackDoesNotHideTheGeographyError()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(P(1, "Work"));
        this.SetupCreateAssigns(2, "Copy");
        this.GeographyFails();
        this._humanProxy.Setup(h => h.DeleteProfileAsync("123456789", 2)).ThrowsAsync(new HttpRequestException("delete failed"));

        var ex = await Assert.ThrowsAsync<PoracleRequestRefusedException>(
            () => this._sut.Duplicate(new DuplicateProfileRequest { FromProfileNo = 1, Name = "Copy" }));

        Assert.Equal("invalid latitude", ex.Message);
    }

    [Fact]
    public async Task ARenameOfAProfileWhoseStoredScheduleV2WouldRefuseSendsNoSchedule()
    {
        // day 0 is stored by a PoracleNG that used 0-6; v2 refuses it. A rename must not resend it.
        var existing = new Profile
        {
            Id = "123456789", ProfileNo = 1, Name = "Old",
            ActiveHours = /*lang=json,strict*/ "[{\"day\":0,\"hours\":8,\"mins\":0}]",
        };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(existing);
        JsonElement captured = default;
        this._humanProxy.Setup(p => p.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<JsonElement>()))
            .Callback<string, JsonElement>((_, body) => captured = body.Clone());

        var result = await this._sut.Update(1, new Profile { Name = "New" });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("New", captured.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, captured.GetProperty("active_hours").ValueKind);
    }

    // --- Control and bidi characters in names -------------------------------------------------------
    // U+202E made "evil" display as "live"; NUL, TAB and line feeds were stored verbatim. Production held
    // none (0 of 457 names), so these are refused on every entry point. Only the INCOMING name is judged.

    [Fact]
    public async Task CreateRefusesANameCarryingABidiOverride()
    {
        var result = await this._sut.Create(new Profile { Name = "evil‮live" });

        Assert.IsType<BadRequestObjectResult>(result);
        this._humanProxy.Verify(p => p.AddProfileAsync(It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    [Fact]
    public async Task CreateStillAcceptsAnEmojiAndAccentedName()
    {
        this.SetupCreateAssigns(1, "🐉 Pokémon");

        Assert.IsType<CreatedAtActionResult>(await this._sut.Create(new Profile { Name = "🐉 Pokémon" }));
    }

    [Theory]
    [InlineData("tab\tbed")]
    [InlineData("nul\u0000")]
    [InlineData("evil‮live")]
    public async Task UpdateRefusesARenameCarryingAControlCharacter(string name)
    {
        var existing = new Profile { Id = "123456789", ProfileNo = 1, Name = "Old" };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(existing);

        Assert.IsType<BadRequestObjectResult>(await this._sut.Update(1, new Profile { Name = name }));
        this._humanProxy.Verify(p => p.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    [Fact]
    public async Task AnEditThatKeepsAnExistingNameIsNotJudgedByTheNewRule()
    {
        // A name stored before this rule existed must not make every later edit of the profile fail -- the
        // active-hours editor resubmits the name it was given.
        var existing = new Profile { Id = "123456789", ProfileNo = 1, Name = "legacy\tname" };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(existing);

        var result = await this._sut.Update(1, new Profile { Name = "legacy\tname", ActiveHours = "[]" });

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task DuplicateRefusesANameCarryingAControlCharacter()
    {
        var result = await this._sut.Duplicate(new DuplicateProfileRequest { FromProfileNo = 1, Name = "copy\nof main" });

        Assert.IsType<BadRequestObjectResult>(result);
        this._humanProxy.Verify(p => p.AddProfileAsync(It.IsAny<string>(), It.IsAny<JsonElement>()), Times.Never);
    }

    // --- What reaches the PATCH: trimmed, and the stored name when the incoming one is blank ----------
    // The name goes to PoracleNG as sent and nothing trims it after, so the controller does.

    private JsonElement CaptureUpdateBody(Profile existing, Profile request)
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", existing.ProfileNo)).ReturnsAsync(existing);
        JsonElement captured = default;
        this._humanProxy.Setup(p => p.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<JsonElement>()))
            .Callback<string, JsonElement>((_, body) => captured = body.Clone());

        this._sut.Update(existing.ProfileNo, request).GetAwaiter().GetResult();
        return captured;
    }

    [Fact]
    public void ARenameReachesTheProxyTrimmed()
    {
        var body = this.CaptureUpdateBody(
            new Profile { Id = "123456789", ProfileNo = 4, Name = "Delta" },
            new Profile { Name = "   Delta spaced   " });

        Assert.Equal("Delta spaced", body.GetProperty("name").GetString());
    }

    [Fact]
    public void ABlankNameIsNotARename()
    {
        var body = this.CaptureUpdateBody(
            new Profile { Id = "123456789", ProfileNo = 4, Name = "Delta" },
            new Profile { Name = "   " });

        Assert.Equal("Delta", body.GetProperty("name").GetString());
    }
}
