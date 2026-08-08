using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Bulk distance updates rewrite every row, so all their uids change. #403 fixed the single-edit case
/// using the create response's 1:1 mapping; that cannot work here because the batch response comes back
/// reordered — submitting lures 161, 162, 163 returned 164, 165, 166 mapping to 163, 162, 161. Pairing by
/// position would repoint a quick pick at somebody else's alarm, so pairing is done on content. See #443.
/// </summary>
public class BulkUidRemapTests
{
    private readonly Mock<IPoracleTrackingProxy> _proxy = new();
    private readonly Mock<ITrackedUidRemapper> _remapper = new();

    private static JsonElement Rows(params (int Uid, int LureId, int Distance)[] rows) =>
        JsonSerializer.SerializeToElement(
            rows.Select(r => new { uid = r.Uid, lure_id = r.LureId, distance = r.Distance, id = "u1" }));

    private Task RunAsync(JsonElement submitted) => BulkUidRemap.ApplyAsync(
        this._proxy.Object, "lure", "u1", submitted, this._remapper.Object, NullLogger.Instance);

    [Fact]
    public async Task PairsByContentEvenWhenTheResponseComesBackReversed()
    {
        // The exact ordering observed against PoracleNG.
        var submitted = Rows((161, 501, 700), (162, 502, 700), (163, 503, 700));
        this._proxy.Setup(p => p.GetByUserAsync("lure", "u1"))
            .ReturnsAsync(Rows((164, 503, 700), (165, 502, 700), (166, 501, 700)));

        await this.RunAsync(submitted);

        this._remapper.Verify(r => r.RemapAsync("u1", "lure", 161, 166), Times.Once);
        this._remapper.Verify(r => r.RemapAsync("u1", "lure", 162, 165), Times.Once);
        this._remapper.Verify(r => r.RemapAsync("u1", "lure", 163, 164), Times.Once);
    }

    [Fact]
    public async Task DoesNotPairByPosition()
    {
        // The wrong answer, spelled out: 161 must not become 164.
        var submitted = Rows((161, 501, 700), (162, 502, 700), (163, 503, 700));
        this._proxy.Setup(p => p.GetByUserAsync("lure", "u1"))
            .ReturnsAsync(Rows((164, 503, 700), (165, 502, 700), (166, 501, 700)));

        await this.RunAsync(submitted);

        this._remapper.Verify(r => r.RemapAsync("u1", "lure", 161, 164), Times.Never);
        this._remapper.Verify(r => r.RemapAsync("u1", "lure", 163, 166), Times.Never);
    }

    [Fact]
    public async Task MatchesEvenThoughOutgoingRowsCarryNoProfileNo()
    {
        // Alarm writes stopped sending profile_no (#411) but rows read back from PoracleNG still have it.
        // Without excluding it the signatures never match and nothing is ever remapped -- which is exactly
        // what happened on the first live run of this fix.
        var submitted = JsonSerializer.SerializeToElement(
            new[] { new { uid = 172, lure_id = 501, distance = 900, id = "u1" } });
        this._proxy.Setup(p => p.GetByUserAsync("lure", "u1")).ReturnsAsync(
            JsonSerializer.SerializeToElement(
                new[] { new { uid = 173, lure_id = 501, distance = 900, id = "u1", profile_no = 0 } }));

        await this.RunAsync(submitted);

        this._remapper.Verify(r => r.RemapAsync("u1", "lure", 172, 173), Times.Once);
    }

    [Fact]
    public async Task IgnoresTheDistanceChangeWhenMatching()
    {
        // Distance is the field that changed, so it cannot take part in identity.
        this._proxy.Setup(p => p.GetByUserAsync("lure", "u1")).ReturnsAsync(Rows((200, 501, 900)));

        await this.RunAsync(Rows((199, 501, 500)));

        this._remapper.Verify(r => r.RemapAsync("u1", "lure", 199, 200), Times.Once);
    }

    [Fact]
    public async Task DoesNothingWhenTheUidSurvived()
    {
        this._proxy.Setup(p => p.GetByUserAsync("lure", "u1")).ReturnsAsync(Rows((10, 501, 900)));

        await this.RunAsync(Rows((10, 501, 500)));

        this._remapper.Verify(
            r => r.RemapAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task RefusesToGuessWhenTwoRowsAreIndistinguishable()
    {
        // Identical apart from distance. Guessing here is exactly the failure this design avoids, and the
        // two are interchangeable anyway once both carry the same distance.
        this._proxy.Setup(p => p.GetByUserAsync("lure", "u1")).ReturnsAsync(Rows((30, 501, 900), (31, 501, 900)));

        await this.RunAsync(Rows((10, 501, 500), (11, 501, 500)));

        this._remapper.Verify(
            r => r.RemapAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task LeavesRowsAloneWhenNoReplacementIsFound()
    {
        this._proxy.Setup(p => p.GetByUserAsync("lure", "u1")).ReturnsAsync(Rows((99, 999, 900)));

        await this.RunAsync(Rows((10, 501, 500)));

        this._remapper.Verify(
            r => r.RemapAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task SwallowsAReadFailure()
    {
        // The distance change already applied upstream; failing here would fail a request that worked.
        this._proxy.Setup(p => p.GetByUserAsync("lure", "u1")).ThrowsAsync(new HttpRequestException("down"));

        await this.RunAsync(Rows((10, 501, 500)));
    }

    [Fact]
    public async Task IgnoresAnEmptySubmission()
    {
        await this.RunAsync(JsonSerializer.SerializeToElement(Array.Empty<object>()));

        this._proxy.Verify(p => p.GetByUserAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
