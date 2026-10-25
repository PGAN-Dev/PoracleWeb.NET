using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Repositories;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// A move is a create on the other human, built from the typed model. A stored property the model has no
/// field for (here a made-up one) was dropped, where the same edit keeps it through
/// <c>TrackingFieldPreserver</c>.
/// </summary>
public class AlarmMoverCarriesStoredFieldsTests
{
    private const string Discord = "100000000000000001";
    private const string Telegram = "200000001";

    private readonly Mock<IPoracleTrackingProxy> _tracking = new();
    private readonly Mock<IFeatureGate> _gate = new();
    private readonly List<JsonElement> _bodies = [];
    private readonly AlarmMover _mover;
    private readonly MonsterService _monsters;

    public AlarmMoverCarriesStoredFieldsTests()
    {
        var linked = new Mock<ILinkedAccount>();
        linked.Setup(l => l.ResolveAsync(It.IsAny<string>())).ReturnsAsync(new LinkedPair(
            Discord,
            new AccountLink(1, Discord, Telegram, LinkSide.Discord, null, null, DateTime.UtcNow),
            new PairSide(LinkSide.Discord, Discord, true, PauseReason.None),
            new PairSide(LinkSide.Telegram, Telegram, true, PauseReason.None)));
        var humans = new Mock<IHumanService>();
        humans.Setup(h => h.GetByIdAsync(It.IsAny<string>())).ReturnsAsync((string id) => new Human { Id = id, CurrentProfileNo = 1 });
        var repo = new Mock<IQuickPickAppliedStateRepository>();
        repo.Setup(r => r.GetByUserAndProfileAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync([]);
        this._gate.Setup(g => g.EnsureEnabledAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        this._tracking.Setup(t => t.CreateAsync("pokemon", Telegram, It.IsAny<JsonElement>()))
            .Callback<string, string, JsonElement>((_, _, body) => this._bodies.Add(body.Clone()))
            .ReturnsAsync(new TrackingCreateResult([77], 0, 0, 1));
        this._monsters = new MonsterService(this._tracking.Object, this._gate.Object, Mock.Of<ITrackedUidRemapper>(), NullLogger<MonsterService>.Instance);
        this._mover = NewMover(linked.Object, humans.Object, repo.Object, this._tracking.Object);
    }

    private static AlarmMover NewMover(ILinkedAccount linked, IHumanService humans, IQuickPickAppliedStateRepository repo, IPoracleTrackingProxy tracking) => new(
        linked, humans, Mock.Of<IPoracleHumanProxy>(), tracking, Mock.Of<IUserAreaPolicy>(),
        new QuickPickUidTransfer(repo, NullLogger<QuickPickUidTransfer>.Instance),
        NullLogger<AlarmMover>.Instance);

    private void Stored(params string[] rows) =>
        this._tracking.Setup(t => t.GetByUserAsync("pokemon", Discord))
            .ReturnsAsync(JsonDocument.Parse("[" + string.Join(",", rows) + "]").RootElement.Clone());

    [Fact]
    public async Task AStoredPropertyTheModelDoesNotDeclareSurvivesAMove()
    {
        this.Stored("""{"uid":5,"id":"100000000000000001","pokemon_id":25,"zz_made_up_flag":true,"zz_made_up_note":"keep me","description":"x"}""");

        var result = await this._mover.MoveAsync<Monster>(this._monsters, "pokemon", Discord,
            new Monster { Uid = 5, PokemonId = 25, Destination = "discord" }, Discord, LinkSide.Telegram);

        Assert.Equal(MoveStatus.Moved, result.Outcome.Status);
        var body = Assert.Single(this._bodies);
        Assert.True(body.GetProperty("zz_made_up_flag").GetBoolean());
        Assert.Equal("keep me", body.GetProperty("zz_made_up_note").GetString());
    }

    [Fact]
    public async Task TheSourceRowsIdentityNeverTravelsWithIt()
    {
        this.Stored("""{"uid":5,"id":"100000000000000001","pokemon_id":25,"profile_no":1,"description":"x","zz_made_up_flag":true}""");

        await this._mover.MoveAsync<Monster>(this._monsters, "pokemon", Discord,
            new Monster { Uid = 5, PokemonId = 25, Destination = "discord" }, Discord, LinkSide.Telegram);

        var body = Assert.Single(this._bodies);
        Assert.False(body.TryGetProperty("uid", out _));
        Assert.False(body.TryGetProperty("profile_no", out _));
        Assert.False(body.TryGetProperty("description", out _));
        Assert.False(body.TryGetProperty("destination", out _));
        Assert.Equal(Telegram, body.GetProperty("id").GetString());
    }

    [Fact]
    public async Task AFailedSourceReadStillMovesWithTheModelAlone()
    {
        this._tracking.Setup(t => t.GetByUserAsync("pokemon", Discord)).ThrowsAsync(new HttpRequestException("down"));

        var result = await this._mover.MoveAsync<Monster>(this._monsters, "pokemon", Discord,
            new Monster { Uid = 5, PokemonId = 25, Destination = "discord" }, Discord, LinkSide.Telegram);

        Assert.Equal(MoveStatus.Moved, result.Outcome.Status);
        Assert.False(Assert.Single(this._bodies).TryGetProperty("zz_made_up_flag", out _));
    }
}
