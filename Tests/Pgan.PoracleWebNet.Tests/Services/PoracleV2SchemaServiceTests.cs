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
/// The area and profile routes #995 moves (<c>/v2/humans/{id}/areas</c>, <c>/location</c>,
/// <c>/profiles</c>, <c>/profiles/{profile_no}</c> and its <c>/copy</c>) were added to both fixtures later,
/// copied verbatim from the live documents of the dev-01 test beds: the 5.2.1 bed and the 5.3.0 bed
/// running the same PoracleNG build as production.
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
        Assert.False(capabilities.ProfileRename);
        Assert.False(capabilities.ProfileCreateReturnsNumber);
        Assert.False(capabilities.AdminHumanRoutes);
        Assert.False(capabilities.InvasionGruntType);
        Assert.False(capabilities.TrackingPing);
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
        Assert.True(capabilities.SetAreasReportsRejected);
        Assert.True(capabilities.AvailableAreas);
        Assert.True(capabilities.ProfileList);
        Assert.True(capabilities.ProfileDelete);
        Assert.True(capabilities.ProfileCopy);
        Assert.True(capabilities.ProfileScopedGeography);
    }

    [Fact]
    public void AReleasedPoracleAlreadyServesTheProfileAndAreaReadsButNotTheReportingSave()
    {
        // Read off 5.2.1's own document: every route #995 moves is there, but its POST .../areas answers
        // {"status":"ok"} rather than what it stored and refused, so the Areas save stays on v1 there.
        var capabilities = PoracleV2SchemaService.Parse(Fixture("5.2.1"));

        Assert.True(capabilities.AvailableAreas);
        Assert.True(capabilities.ProfileList);
        Assert.True(capabilities.ProfileDelete);
        Assert.True(capabilities.ProfileCopy);
        Assert.False(capabilities.SetAreasReportsRejected);
    }

    [Fact]
    public void TheAreasSaveIsReadByWhatItsAnswerCarriesNotByTheRouteExisting()
    {
        const string statusOnly = """
            {"paths":{"/v2/humans/{id}/areas":{"post":{"responses":{"200":{"content":{"application/json":
              {"schema":{"$ref":"#/components/schemas/StatusOKOutputBody"}}}}}}}},
             "components":{"schemas":{"StatusOKOutputBody":{"properties":{"status":{}}}}}}
            """;
        const string reporting = """
            {"paths":{"/v2/humans/{id}/areas":{"post":{"responses":{"200":{"content":{"application/json":
              {"schema":{"$ref":"#/components/schemas/V2SetAreasOutputBody"}}}}}}}},
             "components":{"schemas":{"V2SetAreasOutputBody":{"properties":{"areas":{},"rejected":{}}}}}}
            """;

        Assert.False(PoracleV2SchemaService.Parse(statusOnly).SetAreasReportsRejected);
        Assert.True(PoracleV2SchemaService.Parse(reporting).SetAreasReportsRejected);
    }

    [Fact]
    public void TheDevelopBranchCanWriteANewProfilesGeographyAndAReleasedOneCannot()
    {
        Assert.True(PoracleV2SchemaService.Parse(Fixture("next")).ProfileScopedGeography);

        // 5.2.1 declares ?profile= on the location write but not on the areas write.
        Assert.False(PoracleV2SchemaService.Parse(Fixture("5.2.1")).ProfileScopedGeography);
    }

    [Fact]
    public void ProfileScopedGeographyNeedsTheParameterOnBothWrites()
    {
        const string locationOnly = """
            {"paths":{
              "/v2/humans/{id}/location":{"post":{"parameters":[{"name":"profile","in":"query"}]}},
              "/v2/humans/{id}/areas":{"post":{"parameters":[{"name":"id","in":"path"}],
                "responses":{"200":{"content":{"application/json":{"schema":{"$ref":"#/components/schemas/V2SetAreasOutputBody"}}}}}}}},
             "components":{"schemas":{"V2SetAreasOutputBody":{"properties":{"areas":{},"rejected":{}}}}}}
            """;
        const string both = """
            {"paths":{
              "/v2/humans/{id}/location":{"post":{"parameters":[{"name":"profile","in":"query"}]}},
              "/v2/humans/{id}/areas":{"post":{"parameters":[{"name":"profile","in":"query"}],
                "responses":{"200":{"content":{"application/json":{"schema":{"$ref":"#/components/schemas/V2SetAreasOutputBody"}}}}}}}},
             "components":{"schemas":{"V2SetAreasOutputBody":{"properties":{"areas":{},"rejected":{}}}}}}
            """;
        const string pathParameterOfThatName = """
            {"paths":{
              "/v2/humans/{id}/location":{"post":{"parameters":[{"name":"profile","in":"path"}]}},
              "/v2/humans/{id}/areas":{"post":{"parameters":[{"name":"profile","in":"query"}],
                "responses":{"200":{"content":{"application/json":{"schema":{"$ref":"#/components/schemas/V2SetAreasOutputBody"}}}}}}}},
             "components":{"schemas":{"V2SetAreasOutputBody":{"properties":{"areas":{},"rejected":{}}}}}}
            """;

        Assert.False(PoracleV2SchemaService.Parse(locationOnly).ProfileScopedGeography);
        Assert.False(PoracleV2SchemaService.Parse(pathParameterOfThatName).ProfileScopedGeography);
        Assert.True(PoracleV2SchemaService.Parse(both).ProfileScopedGeography);
    }

    [Fact]
    public void CopyNeedsTheSourceInTheBodyNotJustTheRoute()
    {
        const string routeOnly = """
            {"paths":{"/v2/humans/{id}/profiles/{profile_no}/copy":{"post":{}}},"components":{"schemas":{}}}
            """;

        Assert.False(PoracleV2SchemaService.Parse(routeOnly).ProfileCopy);
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
        Assert.False(capabilities.SetAreasReportsRejected);
        Assert.False(capabilities.AvailableAreas);
        Assert.False(capabilities.ProfileList);
        Assert.False(capabilities.ProfileDelete);
        Assert.False(capabilities.ProfileCopy);
        Assert.False(capabilities.ProfileScopedGeography);
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

    /// <summary>
    /// Max battles merge from 5.4.0 (jfberry/PoracleNG#328), which ships <c>?dry_run</c> on the same
    /// route. The 5.4.0 fixture is upstream's own golden spec at that PR, trimmed to the one operation.
    /// See #1003.
    /// </summary>
    [Fact]
    public void MaxBattlesMergeOnlyWhereTheirCreateDeclaresDryRun()
    {
        Assert.True(PoracleV2SchemaService.Parse(Fixture("5.4.0")).MaxBattleSquashes);
        Assert.False(PoracleV2SchemaService.Parse(Fixture("next")).MaxBattleSquashes);
        Assert.False(PoracleV2SchemaService.Parse(Fixture("5.2.1")).MaxBattleSquashes);
    }

    [Fact]
    public void MaxBattleSquashesIsReadFromTheQueryParameterOnTheMaxBattleCreate()
    {
        const string RouteWithoutIt = """
            {"paths":{"/v2/humans/{id}/tracking/maxbattle":{"post":{"parameters":[
              {"name":"silent","in":"query"}]}}}}
            """;
        const string AnotherTypeOnly = """
            {"paths":{"/v2/humans/{id}/tracking/raid":{"post":{"parameters":[
              {"name":"dry_run","in":"query"}]}}}}
            """;
        const string OnTheEditOnly = """
            {"paths":{"/v2/humans/{id}/tracking/maxbattle/{uid}":{"put":{"parameters":[
              {"name":"dry_run","in":"query"}]}}}}
            """;

        Assert.False(PoracleV2SchemaService.Parse(RouteWithoutIt).MaxBattleSquashes);
        Assert.False(PoracleV2SchemaService.Parse(AnotherTypeOnly).MaxBattleSquashes);
        Assert.False(PoracleV2SchemaService.Parse(OnTheEditOnly).MaxBattleSquashes);
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
