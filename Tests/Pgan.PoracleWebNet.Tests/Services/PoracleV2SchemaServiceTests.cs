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
/// Both halves matter. The 5.2.1 fixture is the no-change case — every capability false, every caller
/// keeping its workaround — and it is the one that fails if a probe is written so loosely it matches
/// anything.
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
        Assert.False(capabilities.ProfileRename);
        Assert.False(capabilities.ProfileCreateReturnsNumber);
        Assert.False(capabilities.AdminHumanRoutes);
        Assert.False(capabilities.InvasionGruntType);
        Assert.False(capabilities.TrackingPing);
        Assert.False(capabilities.PersonalTracking);
        Assert.False(capabilities.PlayerAccounts);
        Assert.False(capabilities.V2LocationLatitudeLongitude);
    }

    [Fact]
    public void TheDevelopBranchCarriesAllOfThem()
    {
        var capabilities = PoracleV2SchemaService.Parse(Fixture("next"));

        Assert.True(capabilities.Read);
        Assert.True(capabilities.TrustedSetAreas);
        Assert.True(capabilities.ProfileRename);
        Assert.True(capabilities.ProfileCreateReturnsNumber);
        Assert.True(capabilities.AdminHumanRoutes);
        Assert.True(capabilities.InvasionGruntType);
        Assert.True(capabilities.TrackingPing);
        Assert.True(capabilities.V2LocationLatitudeLongitude);

        // "All of them" stops at the develop branch. Personal shiny is a separate, later PR (#255), and
        // `next` is a capture from before it, so these two are false here on purpose -- not forgotten.
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
        Assert.True(capabilities.TrackingPing);
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

    [Fact]
    public void TheDevelopBranchNamesLocationsLatitudeLongitude()
    {
        // jfberry/PoracleNG#303 renamed lat/lon on the three location bodies. The fixture is the
        // schemas as a #303 build serves them.
        Assert.True(PoracleV2SchemaService.Parse(Fixture("next")).V2LocationLatitudeLongitude);
    }

    [Fact]
    public void AReleasedPoracleNamesThemLatLon()
    {
        Assert.False(PoracleV2SchemaService.Parse(Fixture("5.2.1")).V2LocationLatitudeLongitude);
    }

    [Fact]
    public void AnUnreadableSchemaSendsLatLon()
    {
        Assert.False(PoracleV2SchemaService.Parse("not json at all").V2LocationLatitudeLongitude);
        Assert.False(PoracleV2SchemaService.Parse(null).V2LocationLatitudeLongitude);
    }

    [Fact]
    public void BothCoordinatesMustBeRenamedNotJustOne()
    {
        const string half = """
            {"components":{"schemas":{"V2LocationBody":{"properties":{"latitude":{},"lon":{}}}}}}
            """;

        Assert.False(PoracleV2SchemaService.Parse(half).V2LocationLatitudeLongitude);
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
        Assert.False(capabilities.ProfileRename);
        Assert.False(capabilities.ProfileCreateReturnsNumber);
        Assert.False(capabilities.AdminHumanRoutes);
        Assert.False(capabilities.InvasionGruntType);
        Assert.False(capabilities.TrackingPing);
        Assert.False(capabilities.V2LocationLatitudeLongitude);
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
    public void AdminHumanRoutesNeedsBothTheListAndTheDelete()
    {
        // Reporting them separately would invite a half-migration that keeps IHumanRepository — and with
        // it PoracleContext — alive for the one method that did not move.
        const string listOnly = """
            {"paths":{"/v2/humans":{"get":{}},"/v2/humans/{id}":{"get":{}}},
             "components":{"schemas":{}}}
            """;
        const string deleteOnly = """
            {"paths":{"/v2/humans":{"post":{}},"/v2/humans/{id}":{"get":{},"delete":{}}},
             "components":{"schemas":{}}}
            """;
        const string both = """
            {"paths":{"/v2/humans":{"get":{},"post":{}},"/v2/humans/{id}":{"get":{},"delete":{}}},
             "components":{"schemas":{"V2HumanSummary":{"properties":{"last_checked":{"type":"string"}}}}}}
            """;

        Assert.False(PoracleV2SchemaService.Parse(listOnly).AdminHumanRoutes);
        Assert.False(PoracleV2SchemaService.Parse(deleteOnly).AdminHumanRoutes);
        Assert.True(PoracleV2SchemaService.Parse(both).AdminHumanRoutes);
    }

    [Fact]
    public void AdminHumanRoutesNeedsTheThreeHashFieldsNotJustTheRoutes()
    {
        // The routes existed from PR #217 on; #230 added last_checked/disabled_date/notes, which the
        // admin grid actually reads. A server with the routes but the pre-#230 summary shape must not
        // report this as available -- it is not meaningfully different from having no route at all.
        const string preHash230 = """
            {"paths":{"/v2/humans":{"get":{}},"/v2/humans/{id}":{"get":{},"delete":{}}},
             "components":{"schemas":{"V2HumanSummary":{"properties":{"id":{"type":"string"}}}}}}
            """;

        Assert.False(PoracleV2SchemaService.Parse(preHash230).AdminHumanRoutes);
    }

    [Fact]
    public void ProfileCreateIsReadByWhatTheBodyCarriesNotByWhatItIsCalled()
    {
        // Testing for "the response is not StatusOKOutputBody" would make an upstream rename read as the
        // capability arriving, and a false positive here means reading a number out of a body that does
        // not carry one. So the question is whether the referenced schema declares profile_no.
        const string renamedButStillEmpty = """
            {"paths":{"/v2/humans/{id}/profiles":{"post":{"responses":{"200":{"content":
              {"application/json":{"schema":{"$ref":"#/components/schemas/SomethingElse"}}}}}}}},
             "components":{"schemas":{"SomethingElse":{"properties":{"status":{"type":"string"}}}}}}
            """;
        const string carriesTheNumber = """
            {"paths":{"/v2/humans/{id}/profiles":{"post":{"responses":{"200":{"content":
              {"application/json":{"schema":{"$ref":"#/components/schemas/Whatever"}}}}}}}},
             "components":{"schemas":{"Whatever":{"properties":{"profile_no":{"type":"integer"}}}}}}
            """;

        Assert.False(PoracleV2SchemaService.Parse(renamedButStillEmpty).ProfileCreateReturnsNumber);
        Assert.True(PoracleV2SchemaService.Parse(carriesTheNumber).ProfileCreateReturnsNumber);
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
