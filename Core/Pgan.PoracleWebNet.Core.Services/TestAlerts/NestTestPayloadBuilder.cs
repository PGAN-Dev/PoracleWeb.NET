using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services.TestAlerts;

/// <summary>
/// Nests don't have an upstream <c>/api/test</c> example payload — PoracleNG may reject
/// nest tests entirely. This builder sends a best-effort shape derived from PoracleJS's
/// nest controller; if the live server returns 400/404 the frontend snackbar surfaces the
/// error and the user knows to treat nest test alerts as unsupported.
/// </summary>
public sealed class NestTestPayloadBuilder : ITestPayloadBuilder
{
    private const int DefaultNestPokemonId = 25;

    public bool CanBuild(string alarmType) => alarmType == "nest";

    public Task<TestPayloadBuildResult> BuildAsync(TestPayloadContext context)
    {
        var pokemonId = context.Alarm.GetInt("pokemon_id", DefaultNestPokemonId);
        if (pokemonId <= 0)
        {
            pokemonId = DefaultNestPokemonId;
        }

        var webhook = new Dictionary<string, object>
        {
            ["nest_id"] = "test-nest-001",
            ["pokemon_id"] = pokemonId,
            ["pokemon_form"] = context.Alarm.GetInt("form", 0),
            ["form"] = context.Alarm.GetInt("form", 0),
            ["name"] = "Test Park",
            ["latitude"] = context.Latitude,
            ["longitude"] = context.Longitude,
            ["pokemon_avg"] = 12.5,
            ["pokemon_count"] = 30,
            ["reset_time"] = context.Now.AddDays(-1).ToUnixTimeSeconds(),
        };

        return Task.FromResult(new TestPayloadBuildResult("nest", webhook));
    }
}
