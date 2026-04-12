using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services.TestAlerts;

/// <summary>
/// Handles both <c>raid</c> and <c>egg</c> alarms — eggs are a raid webhook variant with
/// <c>pokemon_id = 0</c>. Wire <c>type</c> is always <c>raid</c>; Poracle's raid worker
/// branches to the egg DTS template when it sees <c>pokemon_id == 0</c>.
/// </summary>
public sealed class RaidOrEggTestPayloadBuilder : ITestPayloadBuilder
{
    private const int DefaultRaidBossId = 150; // Mewtwo

    public bool CanBuild(string alarmType) => alarmType is "raid" or "egg";

    public Task<TestPayloadBuildResult> BuildAsync(TestPayloadContext context)
    {
        var isEgg = context.AlarmType == "egg";

        var level = context.Alarm.GetInt("level", 5);
        if (level is 9000 or <= 0)
        {
            level = 5;
        }

        int pokemonId;
        int cp;
        if (isEgg)
        {
            pokemonId = 0;
            cp = 0;
        }
        else
        {
            pokemonId = context.Alarm.GetInt("pokemon_id", DefaultRaidBossId);
            if (pokemonId is 0 or 9000)
            {
                pokemonId = DefaultRaidBossId;
            }

            cp = 50000; // Placeholder — Poracle derives the displayed CP from pokemon_id + level.
        }

        var start = context.Now.AddMinutes(10).ToUnixTimeSeconds();
        var end = context.Now.AddMinutes(55).ToUnixTimeSeconds();
        var spawn = context.Now.AddHours(-1).ToUnixTimeSeconds();

        var teamRaw = context.Alarm.GetInt("team", 4);
        var teamId = teamRaw is >= 0 and <= 3 ? teamRaw : 2; // Default Mystic if "any".

        var move = context.Alarm.GetInt("move", 9000);
        var move1 = move != 9000 && !isEgg ? move : 1;

        var webhook = new Dictionary<string, object>
        {
            ["latitude"] = context.Latitude,
            ["longitude"] = context.Longitude,
            ["level"] = level,
            ["pokemon_id"] = pokemonId,
            ["team_id"] = teamId,
            ["cp"] = cp,
            ["start"] = start,
            ["end"] = end,
            ["spawn"] = spawn,
            ["name"] = "Test Gym",
            ["gym_id"] = "test-gym-001",
            ["url"] = string.Empty,
            ["move_1"] = move1,
            ["move_2"] = isEgg ? 2 : 1,
            ["form"] = context.Alarm.GetInt("form", 0),
            ["evolution"] = context.Alarm.GetInt("evolution", 9000) == 9000 ? 0 : context.Alarm.GetInt("evolution", 0),
            ["gender"] = 3,
            ["costume"] = 0,
            ["is_exclusive"] = context.Alarm.GetInt("exclusive", 0) == 1,
            ["is_ex_raid_eligible"] = context.Alarm.GetInt("exclusive", 0) == 1,
            ["ar_scan_eligible"] = true,
        };

        return Task.FromResult(new TestPayloadBuildResult("raid", webhook));
    }
}
