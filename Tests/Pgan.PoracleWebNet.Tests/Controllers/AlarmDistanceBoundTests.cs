using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Pgan.PoracleWebNet.Api.Controllers;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Tests.Controllers;

/// <summary>
/// An alarm's radius has an upper bound: half the Earth's circumference.
/// </summary>
/// <remarks>
/// <c>distance: 99999999</c> was accepted and stored. The bound is the physically impossible rather than a
/// product limit, because production holds a Pokemon rule at 10,000,000 m and it has to stay editable.
/// </remarks>
public class AlarmDistanceBoundTests : ControllerTestBase
{
    private const int JustPastHalfTheEarth = AlarmDistance.MaxMetres + 1;
    private const int LargestRadiusInProduction = 10_000_000;

    /// <summary>Every request DTO that carries an alarm radius, found rather than listed.</summary>
    public static TheoryData<Type> DistanceBearingRequests()
    {
        var data = new TheoryData<Type>();
        foreach (var type in DistanceBearingTypes())
        {
            data.Add(type);
        }

        return data;
    }

    private static List<Type> DistanceBearingTypes() =>
        [.. typeof(MonsterCreate).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract
                && (t.Name.EndsWith("Create", StringComparison.Ordinal)
                    || t.Name.EndsWith("Update", StringComparison.Ordinal)
                    || t == typeof(BulkDistanceRequest))
                && t.GetProperty("Distance") is not null)
            .OrderBy(t => t.Name, StringComparer.Ordinal)];

    [Fact]
    public void EveryAlarmTypeIsCovered()
    {
        // Eleven types, a Create and an Update each, plus the bulk request. A new type that forgot its
        // bound would still be found; this guards against the discovery above silently finding nothing.
        Assert.Equal(23, DistanceBearingTypes().Count);
    }

    [Theory]
    [MemberData(nameof(DistanceBearingRequests))]
    public void ARadiusLargerThanTheEarthIsRefused(Type request)
    {
        Assert.False(IsValidDistance(request, JustPastHalfTheEarth));
        Assert.False(IsValidDistance(request, 99_999_999));
    }

    [Theory]
    [MemberData(nameof(DistanceBearingRequests))]
    public void TheLargestRadiusInProductionIsStillAccepted(Type request)
    {
        Assert.True(IsValidDistance(request, LargestRadiusInProduction));
        Assert.True(IsValidDistance(request, AlarmDistance.MaxMetres));
        Assert.True(IsValidDistance(request, 0));
    }

    [Fact]
    public void TheDefectReportsBodyIsRefusedAndAnEverywhereRuleIsNot()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var typo = JsonSerializer.Deserialize<MonsterCreate>(
            """{"pokemonId":4,"minIv":50,"distance":99999999,"overrideAreas":[],"overrideLocationLabel":""}""", web)!;
        var everywhere = JsonSerializer.Deserialize<MonsterCreate>(
            """{"pokemonId":5000,"minIv":50,"distance":10000000,"overrideAreas":[],"overrideLocationLabel":""}""", web)!;

        Assert.False(Validator.TryValidateObject(typo, new ValidationContext(typo), [], validateAllProperties: true));
        // pokemon_id 5000 is in production too, and stays uncapped.
        Assert.True(Validator.TryValidateObject(everywhere, new ValidationContext(everywhere), [], validateAllProperties: true));
    }

    [Fact]
    public async Task UpdateAllRefusesARadiusLargerThanTheEarth()
    {
        var service = new Mock<IMonsterService>();
        var sut = new MonsterController(service.Object);
        SetupUser(sut);

        Assert.IsType<BadRequestObjectResult>(await sut.UpdateAllDistance(JustPastHalfTheEarth));
        service.Verify(s => s.UpdateDistanceByUserAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAllStillAcceptsTheLargestRadiusInProduction()
    {
        var service = new Mock<IMonsterService>();
        service.Setup(s => s.UpdateDistanceByUserAsync("123456789", 1, LargestRadiusInProduction))
            .ReturnsAsync(DistanceUpdateResult.None with { Updated = 1 });
        var sut = new MonsterController(service.Object);
        SetupUser(sut);

        Assert.IsType<OkObjectResult>(await sut.UpdateAllDistance(LargestRadiusInProduction));
    }

    private static bool IsValidDistance(Type request, int distance)
    {
        var instance = Activator.CreateInstance(request)!;
        var property = request.GetProperty("Distance", BindingFlags.Public | BindingFlags.Instance)!;
        return Validator.TryValidateProperty(
            distance, new ValidationContext(instance) { MemberName = property.Name }, []);
    }
}
