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
    private readonly Mock<IProfileRepository> _profileRepository = new();
    private readonly Mock<Pgan.PoracleWebNet.Api.Services.IUserRoleResolver> _roleResolver = new();
    private readonly Mock<IUserGeofenceRepository> _userGeofenceRepository = new();

    public ProfileControllerTests()
    {
        this._jwtService.Setup(j => j.GenerateTokenWithReplacedProfile(It.IsAny<System.Security.Claims.ClaimsPrincipal>(), It.IsAny<int>(), It.IsAny<bool?>()))
            .Returns("test-jwt-token");
        this._sut = new ProfileController(this._profileService.Object, this._humanService.Object, this._humanProxy.Object, this._profileRepository.Object, this._jwtService.Object, this._roleResolver.Object, this._userGeofenceRepository.Object);
        SetupUser(this._sut);
    }


    /// <summary>
    /// PoracleNG picks the profile number, so the controller creates first and then diffs the profile
    /// list to learn which number it got. Mocks therefore have to return a DIFFERENT list after the
    /// create than before it. See #407.
    /// </summary>
    private void SetupCreateAssigns(int newProfileNo, string name, params Profile[] before)
    {
        var after = before.Append(new Profile { Id = "123456789", ProfileNo = newProfileNo, Name = name }).ToArray();
        this._profileService.SetupSequence(s => s.GetByUserAsync("123456789"))
            .ReturnsAsync(before)
            .ReturnsAsync(after);
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", newProfileNo))
            .ReturnsAsync(new Profile { Id = "123456789", ProfileNo = newProfileNo, Name = name });
    }

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
        this._profileRepository.Setup(r => r.RenameAsync("123456789", 1, "Updated")).ReturnsAsync(true);

        var result = await this._sut.Update(1, new Profile { Name = "Updated" });

        Assert.IsType<OkObjectResult>(result);
        this._humanProxy.Verify(p => p.UpdateProfileAsync("123456789", It.IsAny<JsonElement>()), Times.Once);
        // PoracleNG drops the name, so the rename has to be written directly. See #406.
        this._profileRepository.Verify(r => r.RenameAsync("123456789", 1, "Updated"), Times.Once);
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
        this.SetupCreateAssigns(2, "Main (copy)", sourceProfile);

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
        this.SetupCreateAssigns(2, "Copy", sourceProfile);

        JsonElement? capturedBody = null;
        this._humanProxy
            .Setup(p => p.AddProfileAsync("123456789", It.IsAny<JsonElement>()))
            .Callback<string, JsonElement>((_, body) => capturedBody = body);

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

    [Fact]
    public async Task UpdateIncludesActiveHoursInProxyPayload()
    {
        var existing = new Profile { Id = "123456789", ProfileNo = 1, Name = "Old" };
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(existing);

        JsonElement? capturedBody = null;
        this._humanProxy.Setup(p => p.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<JsonElement>()))
            .Callback<string, JsonElement>((_, body) => capturedBody = body);

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
            .Callback<string, JsonElement>((_, body) => capturedBody = body);

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
        this._profileService.Setup(s => s.GetByUserAsync("123456789")).ReturnsAsync([]);
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(profile);

        JsonElement? capturedBody = null;
        this._humanProxy.Setup(p => p.AddProfileAsync(It.IsAny<string>(), It.IsAny<JsonElement>()))
            .Callback<string, JsonElement>((_, body) => capturedBody = body);

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

    // ── Profile numbering with a gap (#407) ─────────────────────────────────────
    // PoracleWeb predicted max+1; PoracleNG assigns the lowest free number. Any user who had deleted a
    // non-last profile therefore got a different number than PoracleWeb assumed, and create returned an
    // empty body while duplicate copied alarms to a profile_no with no profile row.

    private static Profile P(int no, string name) => new() { Id = "123456789", ProfileNo = no, Name = name };

    [Fact]
    public async Task CreateReturnsTheProfileAtTheNumberPoracleNgActuallyChose()
    {
        // Profiles 0, 1, 3 -> PoracleNG fills the gap at 2, not max+1 = 4.
        this._profileService.SetupSequence(s => s.GetByUserAsync("123456789"))
            .ReturnsAsync([P(0, "Default"), P(1, "Work"), P(3, "Other")])
            .ReturnsAsync([P(0, "Default"), P(1, "Work"), P(2, "Gap Filler"), P(3, "Other")]);
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 2))
            .ReturnsAsync(P(2, "Gap Filler"));

        var result = await this._sut.Create(new Profile { Name = "Gap Filler" });

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(2, Assert.IsType<Profile>(created.Value).ProfileNo);
    }

    [Fact]
    public async Task CreateDoesNotDictateTheProfileNumberToPoracleNg()
    {
        this._profileService.SetupSequence(s => s.GetByUserAsync("123456789"))
            .ReturnsAsync([P(0, "Default")])
            .ReturnsAsync([P(0, "Default"), P(1, "New")]);
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(P(1, "New"));

        JsonElement? sent = null;
        this._humanProxy.Setup(h => h.AddProfileAsync("123456789", It.IsAny<JsonElement>()))
            .Callback<string, JsonElement>((_, b) => sent = b.Clone())
            .Returns(Task.CompletedTask);

        await this._sut.Create(new Profile { Name = "New" });

        Assert.NotNull(sent);
        Assert.False(sent!.Value.TryGetProperty("profileNo", out _));
    }

    [Fact]
    public async Task DuplicateCopiesAlarmsToTheNumberThatActuallyExists()
    {
        // The orphan case: alarms used to be copied to max+1 = 4 while the profile was created at 2.
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 0)).ReturnsAsync(P(0, "Source"));
        this._profileService.SetupSequence(s => s.GetByUserAsync("123456789"))
            .ReturnsAsync([P(0, "Source"), P(1, "Work"), P(3, "Other")])
            .ReturnsAsync([P(0, "Source"), P(1, "Work"), P(2, "Dup"), P(3, "Other")]);
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 2)).ReturnsAsync(P(2, "Dup"));

        await this._sut.Duplicate(new DuplicateProfileRequest { FromProfileNo = 0, Name = "Dup" });

        this._profileService.Verify(s => s.CopyAsync("123456789", 0, 2), Times.Once);
        this._profileService.Verify(s => s.CopyAsync("123456789", 0, 4), Times.Never);
    }

    [Fact]
    public async Task CreateReportsAFailureRatherThanReturningAnEmptyBody()
    {
        // When nothing appears, the create did not happen. Returning 201 with a null body left the SPA
        // list and counter stale while the user was told it worked.
        this._profileService.SetupSequence(s => s.GetByUserAsync("123456789"))
            .ReturnsAsync([P(0, "Default")])
            .ReturnsAsync([P(0, "Default")]);

        var result = await this._sut.Create(new Profile { Name = "Nope" });

        Assert.Equal(Microsoft.AspNetCore.Http.StatusCodes.Status502BadGateway, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    // ── Rename (#406) ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateWritesTheRenameDirectlyBecauseTheProxyDropsIt()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(P(1, "Old"));
        this._profileRepository.Setup(r => r.RenameAsync("123456789", 1, "New")).ReturnsAsync(true);

        await this._sut.Update(1, new Profile { Name = "New" });

        this._profileRepository.Verify(r => r.RenameAsync("123456789", 1, "New"), Times.Once);
    }

    [Fact]
    public async Task UpdateDoesNotRenameWhenTheNameIsUnchanged()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(P(1, "Same"));

        await this._sut.Update(1, new Profile { Name = "Same" });

        this._profileRepository.Verify(
            r => r.RenameAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UpdateDoesNotRenameWhenOnlyActiveHoursChange()
    {
        this._profileService.Setup(s => s.GetByUserAndProfileNoAsync("123456789", 1)).ReturnsAsync(P(1, "Keep"));

        await this._sut.Update(1, new Profile { ActiveHours = "[]" });

        this._profileRepository.Verify(
            r => r.RenameAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
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
        this.SetupCreateAssigns(2, "Copy", source);

        await this._sut.Duplicate(new DuplicateProfileRequest { FromProfileNo = 1, Name = "Copy" });

        this._profileRepository.Verify(r => r.UpdateAsync(It.Is<Profile>(x =>
            x.ProfileNo == 2
            && x.Area == source.Area
            && x.Latitude == source.Latitude
            && x.Longitude == source.Longitude)), Times.Once);
    }
}
