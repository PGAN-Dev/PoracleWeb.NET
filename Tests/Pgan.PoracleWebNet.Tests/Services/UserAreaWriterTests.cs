using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

public class UserAreaWriterTests
{
    private const string Human = "u1";
    private readonly Mock<IPoracleHumanProxy> _proxy = new();
    private readonly Mock<IUserGeofenceRepository> _fences = new();
    private readonly Mock<IPoracleTrackingProxy> _tracking = new();
    private readonly CapturingLogger _log = new();
    private readonly UserAreaWriter _sut;

    private sealed class CapturingLogger : ILogger<UserAreaWriter>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            this.Entries.Add((logLevel, formatter(state, exception)));
    }

    public UserAreaWriterTests()
    {
        this._sut = new UserAreaWriter(
            this._proxy.Object, this._fences.Object, this._tracking.Object, this._log);
        this._proxy.Setup(p => p.GetAreaListAsync(Human)).ReturnsAsync([]);
        this._proxy.Setup(p => p.GetAvailableAreasAsync(Human)).ReturnsAsync([]);
        this._proxy.Setup(p => p.SetAreasTrustedAsync(Human, It.IsAny<IReadOnlyCollection<string>>())).ReturnsAsync([]);
        this._fences.Setup(r => r.GetByHumanIdAsync(Human)).ReturnsAsync([]);
    }

    private void Selectable(params string[] names) =>
        this._proxy.Setup(p => p.GetAvailableAreasAsync(Human))
            .ReturnsAsync(names.Select(n => new V2AreaInfo(n, true, false)).ToList());

    private void NonSelectable(params string[] names) =>
        this._proxy.Setup(p => p.GetAvailableAreasAsync(Human))
            .ReturnsAsync(names.Select(n => new V2AreaInfo(n, false, false)).ToList());

    private void Owns(params string[] names) =>
        this._fences.Setup(r => r.GetByHumanIdAsync(Human))
            .ReturnsAsync(names.Select((n, i) => new UserGeofence { Id = i + 1, HumanId = Human, KojiName = n }).ToList());

    private void Holds(params string[] names) =>
        this._proxy.Setup(p => p.GetAreaListAsync(Human)).ReturnsAsync(names);


    private void VerifyNothingPosted() =>
        this._proxy.Verify(p => p.SetAreasTrustedAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>()), Times.Never);

    // --- refusals ---

    [Fact]
    public async Task RefusesAnotherUsersFenceAndPostsNothing()
    {
        this.Selectable("downtown");

        var ex = await Assert.ThrowsAsync<AreaNotPermittedException>(
            () => this._sut.SaveSelectionAsync(Human, ["Downtown", "Other Fence"]));

        Assert.Equal(["other fence"], ex.Names);
        this.VerifyNothingPosted();
    }

    [Fact]
    public async Task RefusesANonSelectableAdminFenceTheUserDoesNotHold()
    {
        this.NonSelectable("staff only");

        var ex = await Assert.ThrowsAsync<AreaNotPermittedException>(
            () => this._sut.SaveSelectionAsync(Human, ["staff only"]));

        Assert.Equal(["staff only"], ex.Names);
        this.VerifyNothingPosted();
    }

    // --- legitimate cases still pass ---

    [Fact]
    public async Task PostsSelectableOwnAndAlreadyHeldNamesTrusted()
    {
        this.Selectable("downtown");
        this.Owns("my park");
        this.Holds("staff only");
        IReadOnlyCollection<string>? sent = null;
        this._proxy.Setup(p => p.SetAreasTrustedAsync(Human, It.IsAny<IReadOnlyCollection<string>>()))
            .Callback<string, IReadOnlyCollection<string>>((_, a) => sent = a)
            .ReturnsAsync([]);

        var result = await this._sut.SaveSelectionAsync(Human, ["  Downtown ", "My Park", "Staff Only"]);

        Assert.Equal(["downtown", "my park", "staff only"], sent);
        Assert.Equal(["downtown", "my park", "staff only"], result.Stored);
        Assert.Empty(result.Dropped);
    }

    [Fact]
    public async Task AnUnknownHeldNameThatPoracleRejectsDoesNotFailTheSave()
    {
        // 69 production names across 30 users have no fence PoracleNG knows (renamed, &-escaped).
        this.Selectable("downtown");
        this.Holds("old & gone");
        this._proxy.Setup(p => p.SetAreasTrustedAsync(Human, It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(["old & gone"]);

        var result = await this._sut.SaveSelectionAsync(Human, ["downtown", "old & gone"]);

        Assert.Equal(["old & gone"], result.Dropped);
        Assert.Equal(["downtown"], result.Stored);
    }

    [Fact]
    public async Task UnderscoreAndSpaceSpellingsAreTheSameName()
    {
        // PoracleNG NormalizeAreaKey equates the two, so a held "west_end" authorises "west end".
        this.Holds("west_end");

        var result = await this._sut.SaveSelectionAsync(Human, ["west end"]);

        Assert.Equal(["west end"], result.Stored);
    }

    [Fact]
    public async Task AnEmptySelectionClearsTheProfile()
    {
        var result = await this._sut.SaveSelectionAsync(Human, []);

        Assert.Empty(result.Stored);
        this._proxy.Verify(p => p.SetAreasTrustedAsync(Human, It.Is<IReadOnlyCollection<string>>(a => a.Count == 0)), Times.Once);
    }

    // --- add / remove ---

    [Fact]
    public async Task AddPostsTheListWithTheNameAppended()
    {
        this.Holds("downtown");
        IReadOnlyCollection<string>? sent = null;
        this._proxy.Setup(p => p.SetAreasTrustedAsync(Human, It.IsAny<IReadOnlyCollection<string>>()))
            .Callback<string, IReadOnlyCollection<string>>((_, a) => sent = a)
            .ReturnsAsync([]);

        Assert.True(await this._sut.AddToActiveProfileAsync(Human, "My Park"));
        Assert.Equal(["downtown", "my park"], sent);
    }

    [Fact]
    public async Task AddIsANoOpWhenAlreadyHeld()
    {
        this.Holds("my park");

        Assert.False(await this._sut.AddToActiveProfileAsync(Human, "my park"));
        this.VerifyNothingPosted();
    }

    [Fact]
    public async Task AddReturnsFalseWhenPoracleRejectsTheName()
    {
        this._proxy.Setup(p => p.SetAreasTrustedAsync(Human, It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(["my park"]);

        Assert.False(await this._sut.AddToActiveProfileAsync(Human, "my park"));
    }

    [Fact]
    public async Task RemovePostsTheListWithoutTheName()
    {
        this.Holds("downtown", "my park");
        IReadOnlyCollection<string>? sent = null;
        this._proxy.Setup(p => p.SetAreasTrustedAsync(Human, It.IsAny<IReadOnlyCollection<string>>()))
            .Callback<string, IReadOnlyCollection<string>>((_, a) => sent = a)
            .ReturnsAsync([]);

        Assert.True(await this._sut.RemoveFromActiveProfileAsync(Human, "My Park"));
        Assert.Equal(["downtown"], sent);
    }

    [Fact]
    public async Task RemoveIsANoOpWhenNotHeld()
    {
        this.Holds("downtown");

        Assert.False(await this._sut.RemoveFromActiveProfileAsync(Human, "my park"));
        this.VerifyNothingPosted();
    }
    // --- remove everywhere (fence delete) ---

    private void Broadens(Dictionary<string, IReadOnlyList<int>> broadened, params int[] removedFrom) =>
        this._proxy.Setup(p => p.RemoveAreaEverywhereAsync(Human, It.IsAny<string>(), true))
            .ReturnsAsync(new AreaRemovalResult(removedFrom, broadened, true));

    private void HasProfiles(params int[] numbers) =>
        this._proxy.Setup(p => p.GetProfilesAsync(Human)).ReturnsAsync(JsonSerializer.SerializeToElement(new
        {
            profile = numbers.Select(n => new { id = Human, profile_no = n, name = $"P{n}", area = "[]" }).ToArray()
        }));

    /// <summary>
    /// Records every per-profile bulk delete and answers as PoracleNG does: only the uids that are on the
    /// named profile come back deleted.
    /// </summary>
    private List<string> AlarmsLiveOn(Dictionary<(string Type, int Profile), int[]> where)
    {
        var calls = new List<string>();
        this._tracking.Setup(t => t.BulkDeleteByUidsOnProfileAsync(It.IsAny<string>(), Human, It.IsAny<IReadOnlyList<int>>(), It.IsAny<int>()))
            .Callback<string, string, IReadOnlyList<int>, int>((type, _, uids, profile) => calls.Add($"{type}@{profile}:{string.Join(",", uids)}"))
            .ReturnsAsync((string type, string _, IReadOnlyList<int> sent, int profile) =>
                where.TryGetValue((type, profile), out var live) ? sent.Where(live.Contains).ToList() : []);
        return calls;
    }

    [Fact]
    public async Task PreviewCountsTheBroadenedRulesPerTypeAndWritesNothing()
    {
        this.Broadens(new() { ["pokemon"] = [11, 12], ["raid"] = [5], ["gym"] = [] });

        var impact = await this._sut.PreviewRemovalAsync(Human, "My Park");

        Assert.Equal(new Dictionary<string, int> { ["pokemon"] = 2, ["raid"] = 1 }, impact.WidenedByType);
        Assert.Equal(3, impact.Total);
        this._proxy.Verify(p => p.RemoveAreaEverywhereAsync(Human, "my park", true), Times.Once);
        this._proxy.Verify(p => p.RemoveAreaEverywhereAsync(It.IsAny<string>(), It.IsAny<string>(), false), Times.Never);
    }

    [Fact]
    public async Task PreviewOfANameNothingNarrowsIsZero()
    {
        this.Broadens([]);

        var impact = await this._sut.PreviewRemovalAsync(Human, "my park");

        Assert.Empty(impact.WidenedByType);
        Assert.Equal(0, impact.Total);
    }

    [Fact]
    public async Task DeleteRemovesWidenedAlarmsOnEveryProfileBeforeRemovingTheName()
    {
        // Pokemon 11 lives on profile 1, pokemon 12 and the raid on profile 2. PoracleNG's per-profile
        // delete skips a uid that is not on the profile it names, so each one needs its own call.
        this.Broadens(new() { ["pokemon"] = [11, 12], ["raid"] = [5] }, 1);
        this.HasProfiles(1, 2);
        var calls = this.AlarmsLiveOn(new()
        {
            [("pokemon", 1)] = [11],
            [("pokemon", 2)] = [12],
            [("raid", 2)] = [5],
        });
        this._proxy.Setup(p => p.RemoveAreaEverywhereAsync(Human, "my park", false))
            .Callback(() => calls.Add("remove"))
            .ReturnsAsync(new AreaRemovalResult([1], new Dictionary<string, IReadOnlyList<int>>(), false));

        await this._sut.RemoveEverywhereAsync(Human, "My Park", deleteWidenedAlarms: true);

        Assert.Equal(["pokemon@1:11,12", "pokemon@2:12", "raid@1:5", "raid@2:5", "remove"], calls);
    }

    [Fact]
    public async Task DeleteFallsBackToTheActiveProfileWhenTheHumanHasNoProfileRows()
    {
        // PoracleNG targets the current profile when there are no profiles rows, and a rule can be widened
        // there without the name being in humans.area, so removed_from can be empty too.
        this.Broadens(new() { ["pokemon"] = [11] });
        this.HasProfiles();
        this._proxy.Setup(p => p.GetHumanAsync(Human))
            .ReturnsAsync(JsonSerializer.SerializeToElement(new { id = Human, current_profile_no = 3 }));
        var calls = this.AlarmsLiveOn(new() { [("pokemon", 3)] = [11] });

        await this._sut.RemoveEverywhereAsync(Human, "my park", deleteWidenedAlarms: true);

        Assert.Equal(["pokemon@3:11"], calls);
        this._proxy.Verify(p => p.RemoveAreaEverywhereAsync(Human, "my park", false), Times.Once);
    }

    [Fact]
    public async Task DeleteStillRemovesTheNameWhenAWidenedAlarmCouldNotBeFound()
    {
        // Uid 99 is on no profile the human has (deleted between the preview and now, or orphaned). It is
        // logged, and the fence still goes.
        this.Broadens(new() { ["pokemon"] = [11, 99] }, 1);
        this.HasProfiles(1);
        this.AlarmsLiveOn(new() { [("pokemon", 1)] = [11] });

        await this._sut.RemoveEverywhereAsync(Human, "my park", deleteWidenedAlarms: true);

        this._proxy.Verify(p => p.RemoveAreaEverywhereAsync(Human, "my park", false), Times.Once);
        var warning = Assert.Single(this._log.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("99", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("11", warning.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// PoracleNG reports Pokestop event rules under <c>invasion</c> in <c>broadened</c>, but the v2 invasion
    /// delete filters event rows out and skips them. Without a second pass under <c>incident</c> they would
    /// survive "delete those alarms too" and then widen.
    /// </summary>
    [Fact]
    public async Task APokestopEventReportedAsAnInvasionIsDeletedAsAnIncident()
    {
        this.Broadens(new() { ["invasion"] = [5] }, 1);
        this.HasProfiles(1, 2);
        var calls = this.AlarmsLiveOn(new() { [("incident", 1)] = [5] });

        await this._sut.RemoveEverywhereAsync(Human, "my park", deleteWidenedAlarms: true);

        Assert.Equal(["invasion@1:5", "invasion@2:5", "incident@1:5"], calls);
        Assert.DoesNotContain(this._log.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task APlainInvasionIsDeletedWithoutAnIncidentPass()
    {
        this.Broadens(new() { ["invasion"] = [6] }, 1);
        this.HasProfiles(1, 2);
        var calls = this.AlarmsLiveOn(new() { [("invasion", 1)] = [6] });

        await this._sut.RemoveEverywhereAsync(Human, "my park", deleteWidenedAlarms: true);

        Assert.Equal(["invasion@1:6"], calls);
        Assert.DoesNotContain(this._log.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task DeleteWithNothingWidenedDeletesNoAlarms()
    {
        this.Broadens([], 1);

        await this._sut.RemoveEverywhereAsync(Human, "my park", deleteWidenedAlarms: true);

        this._tracking.Verify(t => t.BulkDeleteByUidsOnProfileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<int>()), Times.Never);
        this._proxy.Verify(p => p.RemoveAreaEverywhereAsync(Human, "my park", false), Times.Once);
    }

    [Fact]
    public async Task KeepRemovesTheNameAndDeletesNoAlarms()
    {
        this.Broadens(new() { ["pokemon"] = [11] }, 1);

        await this._sut.RemoveEverywhereAsync(Human, "My Park", deleteWidenedAlarms: false);

        this._tracking.Verify(t => t.BulkDeleteByUidsOnProfileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<int>()), Times.Never);
        this._proxy.Verify(p => p.RemoveAreaEverywhereAsync(Human, "my park", false), Times.Once);
    }

    [Fact]
    public async Task AFailedAlarmDeleteStopsBeforeTheNameIsRemoved()
    {
        this.Broadens(new() { ["pokemon"] = [11] }, 1);
        this.HasProfiles(1);
        this._tracking.Setup(t => t.BulkDeleteByUidsOnProfileAsync("pokemon", Human, It.IsAny<IReadOnlyList<int>>(), 1))
            .ThrowsAsync(new HttpRequestException("down"));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => this._sut.RemoveEverywhereAsync(Human, "my park", deleteWidenedAlarms: true));

        this._proxy.Verify(p => p.RemoveAreaEverywhereAsync(Human, "my park", false), Times.Never);
    }
}
