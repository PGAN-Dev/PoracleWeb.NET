using System.Runtime.CompilerServices;
using System.Text.Json;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Services;

/// <summary>
/// Reading a PoracleNG instance's own <c>/openapi.json</c> for what its <c>/api/v2</c> surface carries.
/// </summary>
/// <remarks>
/// <para>
/// The two fixtures are real documents, trimmed to the parts the parser reads: one from a production
/// 5.2.1, one from a build of PoracleNG's <c>develop</c> at <c>634fe044</c> (PR #217) running on the
/// dev-01 test bed, with <c>V2PokemonRule.ping</c> added from a later live capture against
/// <c>poracle-ng-dev</c> once it carried PR #249 -- the "next" fixture is "develop, trimmed as it is
/// seen over time", not a single frozen commit. A hand-written document would assert this code's own
/// idea of the shape, which is the failure CLAUDE.md records under "Tests: assert what must still work".
/// </para>
/// <para>
/// Both halves matter. The 5.2.1 fixture is the no-change case for PR #217's capabilities — every one of
/// them false, every caller keeping its workaround — and it is the one that fails if a probe is written so
/// loosely it matches anything.
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
    public void AReleasedPoracleCarriesNoneOfThem()
    {
        var capabilities = PoracleV2SchemaService.Parse(Fixture("5.2.1"));

        Assert.True(capabilities.Read);
        Assert.False(capabilities.TrustedSetAreas);
        Assert.False(capabilities.InvasionGruntType);
    }

    [Fact]
    public void TheDevelopBranchCarriesAllOfThem()
    {
        var capabilities = PoracleV2SchemaService.Parse(Fixture("next"));

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
    public void TheFixturesAreRealDocumentsAndNotEachOther()
    {
        // Cheap guard against a fixture being regenerated from the wrong instance, which would make the
        // no-change half of this suite silently assert nothing.
        using var released = JsonDocument.Parse(Fixture("5.2.1"));
        using var next = JsonDocument.Parse(Fixture("next"));

        Assert.Equal("5.2.1", released.RootElement.GetProperty("info").GetProperty("version").GetString());
        Assert.Equal("5.3.0", next.RootElement.GetProperty("info").GetProperty("version").GetString());
    }
}
