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
        Assert.False(capabilities.InvasionGruntType);
        Assert.False(capabilities.PersonalTracking);
        Assert.False(capabilities.PlayerAccounts);
    }

    [Fact]
    public void TheMinimumSupportedReleaseCarriesAllOfThem()
    {
        var capabilities = PoracleV2SchemaService.Parse(Fixture("5.4.0"));

        Assert.True(capabilities.Read);
        Assert.True(capabilities.InvasionGruntType);

        // Personal shiny is a separate PR (#255) that no release carries, so these two are false on 5.4.0
        // on purpose -- not forgotten.
        Assert.False(capabilities.PersonalTracking);
        Assert.False(capabilities.PlayerAccounts);
    }

    /// <summary>
    /// The fixture is a live capture of jfberry/PoracleNG <c>feat/personal-shiny</c> at <c>37cc7a33</c>,
    /// trimmed to <c>V2PokemonRule</c> and the account routes with the schemas they reference (see its
    /// <c>x-source</c>). It replaced a hand-written one; the field and path spellings matched.
    /// </summary>
    [Fact]
    public void ThePersonalShinyBranchCarriesPersonalTrackingAndAccounts()
    {
        var capabilities = PoracleV2SchemaService.Parse(Fixture("personal-shiny"));

        Assert.True(capabilities.Read);
        Assert.True(capabilities.PersonalTracking);
        Assert.True(capabilities.PlayerAccounts);
    }

    [Theory]
    [InlineData("""{"components":{"schemas":{"V2PokemonRule":{"properties":{"personal_shiny":{}}}}},"paths":{}}""")]
    [InlineData("""{"components":{"schemas":{"V2PokemonRule":{"properties":{"personal_background":{}}}}},"paths":{}}""")]
    public void PersonalTrackingNeedsBothFields(string document)
    {
        Assert.False(PoracleV2SchemaService.Parse(document).PersonalTracking);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("post")]
    public void PlayerAccountsNeedsTheListAddAndDeleteOperations(string missing)
    {
        var ops = new[] { "get", "post" }.Where(m => m != missing).ToDictionary(m => m, _ => new { });
        var document = JsonSerializer.Serialize(new
        {
            paths = new Dictionary<string, object>
            {
                ["/v2/humans/{id}/accounts"] = ops,
                ["/v2/humans/{id}/accounts/{player_id}"] = new { delete = new { } },
            },
        });

        Assert.False(PoracleV2SchemaService.Parse(document).PlayerAccounts);
    }

    [Fact]
    public void PlayerAccountsNeedsTheDeleteOperation()
    {
        var document = JsonSerializer.Serialize(new
        {
            paths = new Dictionary<string, object>
            {
                ["/v2/humans/{id}/accounts"] = new { get = new { }, post = new { } },
                ["/v2/humans/{id}/accounts/{player_id}"] = new { put = new { } },
            },
        });

        Assert.False(PoracleV2SchemaService.Parse(document).PlayerAccounts);
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
