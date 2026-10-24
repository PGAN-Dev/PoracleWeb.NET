using System.Text.Json;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// The invasion read folds Pokestop events in, and an event keeps its own uid sequence, so an invasion and an
/// event can share a uid. The preserver must carry fields from the row of the same kind. See #1058.
/// </summary>
public class TrackingFieldPreserverKindTests
{
    private const string Rows =
        """
        [
          {"uid":7,"id":"u1","grunt_type":"kecleon","costume_note":"from-event"},
          {"uid":7,"id":"u1","grunt_type":"blanche","costume_note":"from-invasion"}
        ]
        """;

    private static Mock<IPoracleTrackingProxy> ProxyReading(string rows)
    {
        var proxy = new Mock<IPoracleTrackingProxy>();
        proxy.Setup(p => p.GetByUserAsync("invasion", "u1")).ReturnsAsync(JsonDocument.Parse(rows).RootElement.Clone());
        return proxy;
    }

    private static JsonElement Body(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task AnInvasionEditCarriesFieldsFromTheInvasionRowNotTheEventThatSharesItsUid()
    {
        var proxy = ProxyReading(Rows);

        var body = await TrackingFieldPreserver.PreserveStoredFieldsAsync(
            proxy.Object, "invasion", "u1", 7, Body("""{"uid":7,"id":"u1","grunt_type":"blanche"}"""));

        Assert.Equal("from-invasion", body.GetProperty("costume_note").GetString());
    }

    // No event-edit case: InvasionService sends an event-named model to the incident proxy before the preserver
    // runs, so the preserver only ever sees invasion bodies. The row filter still ignores event rows for them.

    [Fact]
    public async Task APlainInvasionEditStillPreservesWhenNoEventSharesTheUid()
    {
        var proxy = ProxyReading("""[{"uid":7,"id":"u1","grunt_type":"blanche","costume_note":"kept"}]""");

        var body = await TrackingFieldPreserver.PreserveStoredFieldsAsync(
            proxy.Object, "invasion", "u1", 7, Body("""{"uid":7,"id":"u1","grunt_type":"blanche"}"""));

        Assert.Equal("kept", body.GetProperty("costume_note").GetString());
    }

    [Fact]
    public async Task AnInvasionEditNeverBorrowsFromAnEventWhenOnlyTheEventExists()
    {
        var proxy = ProxyReading("""[{"uid":7,"id":"u1","grunt_type":"kecleon","costume_note":"from-event"}]""");

        var body = await TrackingFieldPreserver.PreserveStoredFieldsAsync(
            proxy.Object, "invasion", "u1", 7, Body("""{"uid":7,"id":"u1","grunt_type":"blanche"}"""));

        Assert.False(body.TryGetProperty("costume_note", out _));
    }

    [Fact]
    public async Task OtherTypesStillMatchOnUidAlone()
    {
        var proxy = new Mock<IPoracleTrackingProxy>();
        proxy.Setup(p => p.GetByUserAsync("lure", "u1")).ReturnsAsync(Body("""[{"uid":7,"id":"u1","extra":"kept"}]"""));

        var body = await TrackingFieldPreserver.PreserveStoredFieldsAsync(
            proxy.Object, "lure", "u1", 7, Body("""{"uid":7,"id":"u1","lure_id":501}"""));

        Assert.Equal("kept", body.GetProperty("extra").GetString());
    }
}
