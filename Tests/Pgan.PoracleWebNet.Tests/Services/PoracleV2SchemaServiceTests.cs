using System.Runtime.CompilerServices;
using System.Text.Json;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Reading a PoracleNG instance's own <c>/openapi.json</c> for what its <c>/api/v2</c> surface carries.
/// </summary>
/// <remarks>
/// <para>
/// The fixture is a real document, PoracleNG 5.4.0's own <c>/openapi.json</c>, trimmed to the parts the
/// parser reads. A hand-written document would assert this code's own idea of the shape, which is the
/// failure CLAUDE.md records under "Tests: assert what must still work".
/// </para>
/// <para>
/// The no-change half is the inline documents below: a probe written so loosely that it matches anything
/// would answer true for them.
/// </para>
/// </remarks>
public class PoracleV2SchemaServiceTests
{
    /// <summary>
    /// Located relative to this source file rather than the output directory, matching
    /// <see cref="TrackingV2BoundsTests"/> — the fixtures are test data, not content to ship.
    /// </summary>
    private static string Fixture(string name, [CallerFilePath] string? here = null) =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(here)!, "..", "Fixtures", $"poracleng-openapi-{name}.json"));

    [Fact]
    public void ADocumentThatDeclaresNeitherPropertyCarriesNeitherCapability()
    {
        var capabilities = PoracleV2SchemaService.Parse("""{"components":{"schemas":{"V2SetAreasBody":{"properties":{"areas":{}}},"V2InvasionRule":{"properties":{"type_id":{}}}}},"paths":{}}""");

        Assert.True(capabilities.Read);
        Assert.False(capabilities.TrustedSetAreas);
        Assert.False(capabilities.InvasionGruntType);
    }

    [Fact]
    public void TheMinimumSupportedReleaseCarriesAllOfThem()
    {
        var capabilities = PoracleV2SchemaService.Parse(Fixture("5.4.0"));

        Assert.True(capabilities.Read);
        Assert.True(capabilities.TrustedSetAreas);
        Assert.True(capabilities.InvasionGruntType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("\"a string\"")]
    [InlineData("{}")]
    [InlineData("""{"components":{}}""")]
    [InlineData("""{"components":{"schemas":{}},"paths":{}}""")]
    public void AnythingUnreadableOffersNothing(string? document)
    {
        var capabilities = PoracleV2SchemaService.Parse(document);

        Assert.False(capabilities.TrustedSetAreas);
        Assert.False(capabilities.InvasionGruntType);
    }

    [Fact]
    public void ADocumentThatParsesIsReadEvenWhenItOffersNothing()
    {
        // Read and "everything false" are different answers, and an admin page that wants to say "your
        // Poracle does not have this" rather than "could not ask" needs to tell them apart.
        Assert.True(PoracleV2SchemaService.Parse("""{"components":{"schemas":{}},"paths":{}}""").Read);
        Assert.False(PoracleV2SchemaService.Parse("not json at all").Read);
        Assert.False(PoracleV2SchemaService.Parse(null).Read);
    }

    [Fact]
    public void TheFixtureIsTheMinimumReleasesOwnDocument()
    {
        // Cheap guard against the fixture being regenerated from the wrong instance.
        using var minimum = JsonDocument.Parse(Fixture("5.4.0"));

        Assert.Equal("5.4.0", minimum.RootElement.GetProperty("info").GetProperty("version").GetString());
    }
}
