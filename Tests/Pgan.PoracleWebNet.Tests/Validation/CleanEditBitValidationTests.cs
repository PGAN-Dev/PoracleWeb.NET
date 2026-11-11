using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Validation;

/// <summary>
/// PoracleNG's <c>/api/v2</c> refuses the edit-in-place bit (clean 2, 3, 6 or 7) on quest, invasion, nest, gym
/// and maxbattle ("edit is not supported for gym tracking"), on create and on PUT. Every Create/Update DTO
/// admitted 0-7, so the member saw PoracleNG's raw text. The bit is refused here, by the DTO the controller
/// binds, and the types that can edit (pokemon, raid, egg, lure, pokestop events) keep every value 0-7.
/// </summary>
public class CleanEditBitValidationTests
{
    private static bool ValidateClean(object instance, object? value, out string? message)
    {
        var context = new ValidationContext(instance) { MemberName = "Clean" };
        var results = new List<ValidationResult>();
        var ok = Validator.TryValidateProperty(value, context, results);
        message = results.FirstOrDefault()?.ErrorMessage;
        return ok;
    }

    public static TheoryData<string, int> NoEditTypesWithEditBit()
    {
        var data = new TheoryData<string, int>();
        foreach (var type in new[] { "quest", "invasion", "nest", "gym", "maxbattle" })
        {
            foreach (var clean in new[] { 2, 3, 6, 7 })
            {
                data.Add(type, clean);
            }
        }

        return data;
    }

    public static TheoryData<string, int> NoEditTypesWithoutEditBit()
    {
        var data = new TheoryData<string, int>();
        foreach (var type in new[] { "quest", "invasion", "nest", "gym", "maxbattle" })
        {
            foreach (var clean in new[] { 0, 1, 4, 5 })
            {
                data.Add(type, clean);
            }
        }

        return data;
    }

    public static TheoryData<string, int> EditTypes()
    {
        var data = new TheoryData<string, int>();
        foreach (var type in new[] { "monster", "raid", "egg", "lure", "pokestopevent" })
        {
            foreach (var clean in new[] { 0, 1, 2, 3, 4, 5, 6, 7 })
            {
                data.Add(type, clean);
            }
        }

        return data;
    }

    private static object CreateOf(string type) => type switch
    {
        "monster" => new MonsterCreate(),
        "raid" => new RaidCreate(),
        "egg" => new EggCreate(),
        "lure" => new LureCreate(),
        "pokestopevent" => new PokestopEventCreate(),
        "quest" => new QuestCreate(),
        "invasion" => new InvasionCreate(),
        "nest" => new NestCreate(),
        "gym" => new GymCreate(),
        "maxbattle" => new MaxBattleCreate(),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static object UpdateOf(string type) => type switch
    {
        "monster" => new MonsterUpdate(),
        "raid" => new RaidUpdate(),
        "egg" => new EggUpdate(),
        "lure" => new LureUpdate(),
        "pokestopevent" => new PokestopEventUpdate(),
        "quest" => new QuestUpdate(),
        "invasion" => new InvasionUpdate(),
        "nest" => new NestUpdate(),
        "gym" => new GymUpdate(),
        "maxbattle" => new MaxBattleUpdate(),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    [Theory]
    [MemberData(nameof(NoEditTypesWithEditBit))]
    public void CreateRefusesTheEditBitWithOurOwnMessage(string type, int clean)
    {
        Assert.False(ValidateClean(CreateOf(type), clean, out var message));
        Assert.Contains("edit-in-place", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PoracleNG", message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(NoEditTypesWithEditBit))]
    public void UpdateRefusesTheEditBitWithOurOwnMessage(string type, int clean)
    {
        Assert.False(ValidateClean(UpdateOf(type), clean, out var message));
        Assert.Contains("edit-in-place", message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(NoEditTypesWithoutEditBit))]
    public void TheOtherBitsStillPassOnTheTypesThatCannotEdit(string type, int clean)
    {
        Assert.True(ValidateClean(CreateOf(type), clean, out _));
        Assert.True(ValidateClean(UpdateOf(type), clean, out _));
    }

    [Fact]
    public void AnUpdateThatLeavesCleanUnsetIsNotRefused()
    {
        foreach (var type in new[] { "quest", "invasion", "nest", "gym", "maxbattle" })
        {
            Assert.True(ValidateClean(UpdateOf(type), null, out _), type);
        }
    }

    [Theory]
    [MemberData(nameof(EditTypes))]
    public void TheTypesThatCanEditKeepEveryValue(string type, int clean)
    {
        Assert.True(ValidateClean(CreateOf(type), clean, out _));
        Assert.True(ValidateClean(UpdateOf(type), clean, out _));
    }

    // The translator is the last stop before PoracleNG, whatever path built the row.

    private static JsonElement Row(string type, int clean)
    {
        var json = type switch
        {
            "quest" => $$"""{"reward_type":7,"reward":25,"distance":0,"clean":{{clean}}}""",
            "invasion" => $$"""{"grunt_type":"water","gender":0,"distance":0,"clean":{{clean}}}""",
            "nest" => $$"""{"pokemon_id":25,"distance":0,"clean":{{clean}}}""",
            "gym" => $$"""{"team":4,"distance":0,"clean":{{clean}}}""",
            "maxbattle" => $$"""{"pokemon_id":9000,"level":5,"distance":0,"clean":{{clean}}}""",
            "lure" => $$"""{"lure_id":501,"distance":0,"clean":{{clean}}}""",
            "egg" => $$"""{"level":5,"distance":0,"clean":{{clean}}}""",
            "raid" => $$"""{"pokemon_id":150,"level":9000,"distance":0,"clean":{{clean}}}""",
            "pokemon" => $$"""{"pokemon_id":25,"distance":0,"clean":{{clean}}}""",
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Theory]
    [InlineData("quest")]
    [InlineData("invasion")]
    [InlineData("nest")]
    [InlineData("gym")]
    [InlineData("maxbattle")]
    public void TheTranslatorRefusesTheEditBitOnATypeThatCannotEdit(string type)
    {
        Assert.False(TrackingV2Translator.TryTranslate(type, Row(type, 3), null, out _, out var why));
        Assert.Contains("edit-in-place", why, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("quest")]
    [InlineData("invasion")]
    [InlineData("nest")]
    [InlineData("gym")]
    [InlineData("maxbattle")]
    public void TheTranslatorStillSendsAutoDeleteAndSummaryOnATypeThatCannotEdit(string type)
    {
        Assert.True(TrackingV2Translator.TryTranslate(type, Row(type, 5), null, out var body, out var why), why);
        Assert.True(body.GetProperty("clean").GetBoolean());
        Assert.False(body.GetProperty("edit").GetBoolean());
        Assert.True(body.GetProperty("summary").GetBoolean());
    }

    [Theory]
    [InlineData("pokemon")]
    [InlineData("raid")]
    [InlineData("egg")]
    [InlineData("lure")]
    public void TheTranslatorStillSendsTheEditBitOnATypeThatCanEdit(string type)
    {
        Assert.True(TrackingV2Translator.TryTranslate(type, Row(type, 7), null, out var body, out var why), why);
        Assert.True(body.GetProperty("edit").GetBoolean());
    }
}
