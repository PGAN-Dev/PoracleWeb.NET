using System.Text.Json;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Models;

public class MoveStatusSerializationTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(MoveStatus.Moved, "moved")]
    [InlineData(MoveStatus.Conflict, "conflict")]
    [InlineData(MoveStatus.SourceRemains, "sourceRemains")]
    [InlineData(MoveStatus.Refused, "refused")]
    [InlineData(MoveStatus.Failed, "failed")]
    [InlineData(MoveStatus.AlreadyThere, "alreadyThere")]
    public void StatusIsCamelCaseOnTheWire(MoveStatus status, string wire)
    {
        var json = JsonSerializer.Serialize(new MoveOutcome(7, status), Web);

        Assert.Contains($"\"status\":\"{wire}\"", json);
    }

    [Fact]
    public void StatusStillReadsBack()
    {
        var back = JsonSerializer.Deserialize<MoveOutcome>("{\"uid\":1,\"status\":\"sourceRemains\"}", Web);

        Assert.Equal(MoveStatus.SourceRemains, back!.Status);
    }
}
