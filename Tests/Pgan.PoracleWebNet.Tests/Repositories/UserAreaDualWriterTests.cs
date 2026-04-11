using Microsoft.EntityFrameworkCore;
using Pgan.PoracleWebNet.Core.Repositories;
using Pgan.PoracleWebNet.Data;
using Pgan.PoracleWebNet.Data.Entities;

namespace Pgan.PoracleWebNet.Tests.Repositories;

/// <summary>
/// Unit tests for <see cref="UserAreaDualWriter"/>. These run against EF Core's in-memory
/// provider so we exercise the real read-modify-write logic (not a Moq'd surface) — that's
/// the only way to guarantee the <c>humans.area</c> + <c>profiles.area</c> dual-write is
/// actually atomic in a single SaveChanges call.
/// </summary>
public partial class UserAreaDualWriterTests : IDisposable
{
    private readonly PoracleContext _context;
    private readonly UserAreaDualWriter _sut;

    public UserAreaDualWriterTests()
    {
        var options = new DbContextOptionsBuilder<PoracleContext>()
            .UseInMemoryDatabase(databaseName: $"UserAreaDualWriter_{Guid.NewGuid()}")
            .Options;
        this._context = new PoracleContext(options);
        this._sut = new UserAreaDualWriter(this._context);
    }

    public void Dispose()
    {
        this._context.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<HumanEntity> SeedHumanAsync(string id = "u1", int currentProfile = 1, string area = "[]")
    {
        var human = new HumanEntity
        {
            Id = id,
            Name = "Test User",
            Type = "discord",
            Enabled = 1,
            Area = area,
            Latitude = 0,
            Longitude = 0,
            CurrentProfileNo = currentProfile,
            CommunityMembership = "[]",
        };
        this._context.Humans.Add(human);
        await this._context.SaveChangesAsync();
        return human;
    }

    private async Task<ProfileEntity> SeedProfileAsync(string humanId, int profileNo, string area = "[]")
    {
        var profile = new ProfileEntity
        {
            Id = humanId,
            ProfileNo = profileNo,
            Name = $"Profile {profileNo}",
            Area = area,
        };
        this._context.Profiles.Add(profile);
        await this._context.SaveChangesAsync();
        return profile;
    }

    // --- AddAreaToActiveProfileAsync ---

    [Fact]
    public async Task AddAreaToActiveProfileAsyncAddsToBothRows()
    {
        await this.SeedHumanAsync("u1", currentProfile: 1, area: "[\"downtown\"]");
        await this.SeedProfileAsync("u1", 1, area: "[\"downtown\"]");

        var modified = await this._sut.AddAreaToActiveProfileAsync("u1", "my park");

        Assert.True(modified);
        var human = await this._context.Humans.SingleAsync(h => h.Id == "u1");
        var profile = await this._context.Profiles.SingleAsync(p => p.Id == "u1" && p.ProfileNo == 1);
        Assert.Contains("my park", human.Area);
        Assert.Contains("downtown", human.Area);
        Assert.Contains("my park", profile.Area);
        Assert.Contains("downtown", profile.Area);
    }

    [Fact]
    public async Task AddAreaToActiveProfileAsyncLowercasesTheName()
    {
        await this.SeedHumanAsync();
        await this.SeedProfileAsync("u1", 1);

        await this._sut.AddAreaToActiveProfileAsync("u1", "My Park");

        var human = await this._context.Humans.SingleAsync(h => h.Id == "u1");
        Assert.Contains("my park", human.Area);
        Assert.DoesNotContain("My Park", human.Area);
    }

    [Fact]
    public async Task AddAreaToActiveProfileAsyncIsIdempotent()
    {
        await this.SeedHumanAsync(area: "[\"my park\"]");
        await this.SeedProfileAsync("u1", 1, area: "[\"my park\"]");

        var modified = await this._sut.AddAreaToActiveProfileAsync("u1", "my park");

        Assert.False(modified);
    }

    [Fact]
    public async Task AddAreaToActiveProfileAsyncWritesToCurrentProfileNotProfile1()
    {
        // Guards against a common off-by-one: always use human.CurrentProfileNo, never hardcode 1.
        await this.SeedHumanAsync(currentProfile: 3);
        await this.SeedProfileAsync("u1", 1, area: "[]");
        await this.SeedProfileAsync("u1", 3, area: "[]");

        await this._sut.AddAreaToActiveProfileAsync("u1", "my park");

        var profile1 = await this._context.Profiles.SingleAsync(p => p.Id == "u1" && p.ProfileNo == 1);
        var profile3 = await this._context.Profiles.SingleAsync(p => p.Id == "u1" && p.ProfileNo == 3);
        Assert.DoesNotContain("my park", profile1.Area);
        Assert.Contains("my park", profile3.Area);
    }

    [Fact]
    public async Task AddAreaToActiveProfileAsyncThrowsWhenHumanMissing()
    {
        // TOCTOU guard: if the human row has been deleted mid-request, we must throw so the
        // controller returns 404 rather than pretending the toggle succeeded.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => this._sut.AddAreaToActiveProfileAsync("ghost", "my park"));
        Assert.Contains("ghost", ex.Message);
    }

    [Fact]
    public async Task AddAreaToActiveProfileAsyncStillCommitsToHumanWhenProfileRowMissing()
    {
        // Edge case: user has a human row but no profile row for their current_profile_no
        // (should be impossible, but be permissive — human.area still updates).
        await this.SeedHumanAsync(currentProfile: 99);

        var modified = await this._sut.AddAreaToActiveProfileAsync("u1", "my park");

        Assert.True(modified);
        var human = await this._context.Humans.SingleAsync(h => h.Id == "u1");
        Assert.Contains("my park", human.Area);
    }

    // --- RemoveAreaFromActiveProfileAsync ---

    [Fact]
    public async Task RemoveAreaFromActiveProfileAsyncRemovesFromBothRows()
    {
        await this.SeedHumanAsync(area: "[\"downtown\",\"my park\"]");
        await this.SeedProfileAsync("u1", 1, area: "[\"downtown\",\"my park\"]");

        var modified = await this._sut.RemoveAreaFromActiveProfileAsync("u1", "my park");

        Assert.True(modified);
        var human = await this._context.Humans.SingleAsync(h => h.Id == "u1");
        var profile = await this._context.Profiles.SingleAsync(p => p.Id == "u1" && p.ProfileNo == 1);
        Assert.DoesNotContain("my park", human.Area);
        Assert.Contains("downtown", human.Area);
        Assert.DoesNotContain("my park", profile.Area);
    }

    [Fact]
    public async Task RemoveAreaFromActiveProfileAsyncWritesEmptyArrayWhenLastItemRemoved()
    {
        await this.SeedHumanAsync(area: "[\"my park\"]");
        await this.SeedProfileAsync("u1", 1, area: "[\"my park\"]");

        await this._sut.RemoveAreaFromActiveProfileAsync("u1", "my park");

        var human = await this._context.Humans.SingleAsync(h => h.Id == "u1");
        Assert.Equal("[]", human.Area);
    }

    [Fact]
    public async Task RemoveAreaFromActiveProfileAsyncIsIdempotent()
    {
        await this.SeedHumanAsync(area: "[\"other\"]");
        await this.SeedProfileAsync("u1", 1, area: "[\"other\"]");

        var modified = await this._sut.RemoveAreaFromActiveProfileAsync("u1", "missing");

        Assert.False(modified);
    }

    [Fact]
    public async Task RemoveAreaFromActiveProfileAsyncThrowsWhenHumanMissing() => await Assert.ThrowsAsync<InvalidOperationException>(
            () => this._sut.RemoveAreaFromActiveProfileAsync("ghost", "my park"));

    // --- AddAreasToActiveProfileAsync (bulk) ---

    [Fact]
    public async Task AddAreasToActiveProfileAsyncBulkAddsAllNamesInOneWrite()
    {
        await this.SeedHumanAsync(area: "[\"downtown\"]");
        await this.SeedProfileAsync("u1", 1, area: "[\"downtown\"]");

        var modified = await this._sut.AddAreasToActiveProfileAsync(
            "u1", ["my park", "my square", "downtown"]);

        Assert.True(modified);
        var human = await this._context.Humans.SingleAsync(h => h.Id == "u1");
        var profile = await this._context.Profiles.SingleAsync(p => p.Id == "u1" && p.ProfileNo == 1);
        Assert.Contains("my park", human.Area);
        Assert.Contains("my square", human.Area);
        Assert.Contains("downtown", human.Area); // pre-existing, not duplicated
        Assert.Contains("my park", profile.Area);
        Assert.Contains("my square", profile.Area);
    }

    [Fact]
    public async Task AddAreasToActiveProfileAsyncReturnsFalseWhenEveryNameAlreadyPresent()
    {
        await this.SeedHumanAsync(area: "[\"my park\",\"my square\"]");
        await this.SeedProfileAsync("u1", 1, area: "[\"my park\",\"my square\"]");

        var modified = await this._sut.AddAreasToActiveProfileAsync(
            "u1", ["my park", "my square"]);

        Assert.False(modified);
    }

    [Fact]
    public async Task AddAreasToActiveProfileAsyncReturnsFalseForEmptyInput()
    {
        await this.SeedHumanAsync();

        var modified = await this._sut.AddAreasToActiveProfileAsync("u1", []);

        Assert.False(modified);
    }

    [Fact]
    public async Task AddAreasToActiveProfileAsyncThrowsWhenHumanMissing() => await Assert.ThrowsAsync<InvalidOperationException>(
            () => this._sut.AddAreasToActiveProfileAsync("ghost", ["my park"]));

    [Fact]
    public async Task AddAreasToActiveProfileAsyncDeduplicatesCaseInsensitiveInput()
    {
        await this.SeedHumanAsync();
        await this.SeedProfileAsync("u1", 1);

        await this._sut.AddAreasToActiveProfileAsync(
            "u1", ["My Park", "my park", "MY PARK"]);

        var human = await this._context.Humans.SingleAsync(h => h.Id == "u1");
        // Exactly one "my park" entry — not three.
        Assert.Single(MyRegex().Matches(human.Area));
    }

    // --- RemoveAreaFromAllProfilesAsync ---

    [Fact]
    public async Task RemoveAreaFromAllProfilesAsyncRemovesFromEveryProfile()
    {
        await this.SeedHumanAsync(area: "[\"my park\",\"other\"]");
        await this.SeedProfileAsync("u1", 1, area: "[\"my park\",\"alpha\"]");
        await this.SeedProfileAsync("u1", 2, area: "[\"my park\"]");
        await this.SeedProfileAsync("u1", 3, area: "[\"beta\"]"); // no "my park" — should be untouched

        var modified = await this._sut.RemoveAreaFromAllProfilesAsync("u1", "my park");

        Assert.True(modified);
        var human = await this._context.Humans.SingleAsync(h => h.Id == "u1");
        var p1 = await this._context.Profiles.SingleAsync(p => p.Id == "u1" && p.ProfileNo == 1);
        var p2 = await this._context.Profiles.SingleAsync(p => p.Id == "u1" && p.ProfileNo == 2);
        var p3 = await this._context.Profiles.SingleAsync(p => p.Id == "u1" && p.ProfileNo == 3);

        Assert.DoesNotContain("my park", human.Area);
        Assert.DoesNotContain("my park", p1.Area);
        Assert.Contains("alpha", p1.Area);
        Assert.Equal("[]", p2.Area);
        Assert.Contains("beta", p3.Area); // untouched
    }

    [Fact]
    public async Task RemoveAreaFromAllProfilesAsyncDoesNotThrowWhenHumanMissing()
    {
        // Deletion should be permissive — no human row = no work.
        var modified = await this._sut.RemoveAreaFromAllProfilesAsync("ghost", "my park");
        Assert.False(modified);
    }

    [Fact]
    public async Task RemoveAreaFromAllProfilesAsyncReturnsFalseWhenNameNotPresentAnywhere()
    {
        await this.SeedHumanAsync(area: "[\"other\"]");
        await this.SeedProfileAsync("u1", 1, area: "[\"other\"]");

        var modified = await this._sut.RemoveAreaFromAllProfilesAsync("u1", "missing");

        Assert.False(modified);
    }

    // --- Input validation ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddAreaToActiveProfileAsyncRejectsInvalidHumanId(string? humanId) =>
        // Guard clause: the writer is a public interface and shouldn't accept garbage humanIds
        // even though the current call sites always pass valid values.
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => this._sut.AddAreaToActiveProfileAsync(humanId!, "my park"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddAreaToActiveProfileAsyncRejectsInvalidAreaName(string? areaName)
    {
        await this.SeedHumanAsync();
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => this._sut.AddAreaToActiveProfileAsync("u1", areaName!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoveAreaFromActiveProfileAsyncRejectsInvalidHumanId(string? humanId) => await Assert.ThrowsAnyAsync<ArgumentException>(
            () => this._sut.RemoveAreaFromActiveProfileAsync(humanId!, "my park"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoveAreaFromActiveProfileAsyncRejectsInvalidAreaName(string? areaName)
    {
        await this.SeedHumanAsync();
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => this._sut.RemoveAreaFromActiveProfileAsync("u1", areaName!));
    }

    [Fact]
    public async Task AddAreasToActiveProfileAsyncRejectsNullCollection() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => this._sut.AddAreasToActiveProfileAsync("u1", null!));

    [Fact]
    public async Task AddAreasToActiveProfileAsyncStripsBlankEntriesFromInput()
    {
        // Whitespace-only names are indistinguishable from "no area" — they shouldn't be
        // persisted even if a caller accidentally includes them.
        await this.SeedHumanAsync();
        await this.SeedProfileAsync("u1", 1);

        var modified = await this._sut.AddAreasToActiveProfileAsync("u1", ["my park", "", "   ", "my square"]);

        Assert.True(modified);
        var human = await this._context.Humans.SingleAsync(h => h.Id == "u1");
        Assert.Contains("my park", human.Area);
        Assert.Contains("my square", human.Area);
        // No empty string in the serialized array
        Assert.DoesNotContain("\"\"", human.Area);
    }

    [Fact]
    public async Task AddAreasToActiveProfileAsyncReturnsFalseWhenAllInputIsBlank()
    {
        await this.SeedHumanAsync();
        var modified = await this._sut.AddAreasToActiveProfileAsync("u1", ["", "   "]);
        Assert.False(modified);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoveAreaFromAllProfilesAsyncRejectsInvalidHumanId(string? humanId) =>
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => this._sut.RemoveAreaFromAllProfilesAsync(humanId!, "my park"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoveAreaFromAllProfilesAsyncRejectsInvalidAreaName(string? areaName) =>
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => this._sut.RemoveAreaFromAllProfilesAsync("u1", areaName!));

    [System.Text.RegularExpressions.GeneratedRegex("my park")]
    private static partial System.Text.RegularExpressions.Regex MyRegex();
}
