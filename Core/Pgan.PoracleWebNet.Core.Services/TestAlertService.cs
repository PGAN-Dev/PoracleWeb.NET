using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public partial class TestAlertService(
    IPoracleApiProxy apiProxy,
    IPoracleTrackingProxy trackingProxy,
    IPoracleHumanProxy humanProxy,
    ILogger<TestAlertService> logger) : ITestAlertService
{
    private static readonly HashSet<string> ValidTypes = ["pokemon", "raid", "egg", "quest", "invasion", "lure", "nest", "gym"];

    private readonly IPoracleApiProxy _apiProxy = apiProxy;
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly ILogger<TestAlertService> _logger = logger;
    private readonly IPoracleTrackingProxy _trackingProxy = trackingProxy;

    public async Task SendTestAlertAsync(string userId, string alarmType, int uid)
    {
        if (!ValidTypes.Contains(alarmType))
        {
            throw new ArgumentException($"Invalid alarm type: {alarmType}", nameof(alarmType));
        }

        // Fetch alarm data and human data in parallel
        var alarmTask = this._trackingProxy.GetByUserAsync(alarmType, userId);
        var humanTask = this._humanProxy.GetHumanAsync(userId);
        await Task.WhenAll(alarmTask, humanTask);

        var allAlarms = alarmTask.Result;
        var human = humanTask.Result
            ?? throw new InvalidOperationException("User not found");

        // Find the specific alarm by uid
        var alarm = FindAlarmByUid(allAlarms, uid)
            ?? throw new KeyNotFoundException($"Alarm with uid {uid} not found");

        // Build the test request — use the alarm's template so PoracleNG renders with the right DTS
        var target = BuildTarget(userId, human);
        target.Template = GetString(alarm, "template", "1");
        var request = new TestAlertRequest
        {
            Type = alarmType,
            Target = target,
            Webhook = BuildMockWebhook(alarmType, alarm, target),
        };

        LogSendingTestAlert(this._logger, alarmType, uid, userId);
        await this._apiProxy.SendTestAlertAsync(request);
    }

    private static TestAlertTarget BuildTarget(string userId, JsonElement human)
    {
        var target = new TestAlertTarget { Id = userId };

        if (human.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
        {
            target.Name = name.GetString() ?? string.Empty;
        }

        if (human.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
        {
            target.Type = type.GetString() ?? "discord:user";
        }

        if (human.TryGetProperty("language", out var lang) && lang.ValueKind == JsonValueKind.String)
        {
            target.Language = lang.GetString() ?? "en";
        }

        if (human.TryGetProperty("latitude", out var lat) && lat.TryGetDouble(out var latVal))
        {
            target.Latitude = latVal;
        }

        if (human.TryGetProperty("longitude", out var lon) && lon.TryGetDouble(out var lonVal))
        {
            target.Longitude = lonVal;
        }

        return target;
    }

    private static Dictionary<string, object> BuildMockWebhook(string alarmType, JsonElement alarm, TestAlertTarget target)
    {
        // Extract the template from the alarm if available
        var template = "default";
        if (alarm.TryGetProperty("template", out var tmpl) && tmpl.ValueKind == JsonValueKind.String)
        {
            template = tmpl.GetString() ?? "default";
        }

        var now = DateTimeOffset.UtcNow;
        var disappearTime = now.AddMinutes(30).ToUnixTimeSeconds();
        var raidEndTime = now.AddMinutes(45).ToUnixTimeSeconds();

        // Use user's location as the default, fall back to a sensible default if unset
        const double coordinateEpsilon = 1e-6;
        var lat = Math.Abs(target.Latitude) > coordinateEpsilon ? target.Latitude : 40.7128;
        var lon = Math.Abs(target.Longitude) > coordinateEpsilon ? target.Longitude : -74.006;

        return alarmType switch
        {
            "pokemon" => BuildPokemonWebhook(alarm, lat, lon, disappearTime, template),
            "raid" => BuildRaidWebhook(alarm, lat, lon, raidEndTime, template),
            "egg" => BuildEggWebhook(alarm, lat, lon, now, raidEndTime, template),
            "quest" => BuildQuestWebhook(alarm, lat, lon, template),
            "invasion" => BuildInvasionWebhook(alarm, lat, lon, template),
            "lure" => BuildLureWebhook(alarm, lat, lon, template),
            "nest" => BuildNestWebhook(alarm, lat, lon, template),
            "gym" => BuildGymWebhook(alarm, lat, lon, template),
            _ => []
        };
    }

    private static Dictionary<string, object> BuildPokemonWebhook(
        JsonElement alarm, double lat, double lon, long disappearTime, string template)
    {
        var pokemonId = GetInt(alarm, "pokemon_id", 25);
        if (pokemonId == 0)
        {
            pokemonId = 25; // "All Pokemon" alarm → use Pikachu for test
        }

        var form = GetInt(alarm, "form", 0);

        return new Dictionary<string, object>
        {
            ["encounter_id"] = $"test-{Guid.NewGuid():N}",
            ["pokemon_id"] = pokemonId,
            ["form"] = form,
            ["latitude"] = lat,
            ["longitude"] = lon,
            ["disappear_time"] = disappearTime,
            ["disappear_time_verified"] = true,
            ["first_seen_timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["individual_attack"] = 15,
            ["individual_defense"] = 15,
            ["individual_stamina"] = 15,
            ["pokemon_level"] = 35,
            ["cp"] = 2500,
            ["gender"] = GetInt(alarm, "gender", 1),
            ["weather"] = 0,
            ["weight"] = 6.5,
            ["height"] = 0.4,
            ["size"] = GetInt(alarm, "size", 3),
            ["move_1"] = 1,
            ["move_2"] = 1,
            ["pvp_rankings_great_league"] = new[] { new { pokemon = pokemonId, form, rank = 1, cp = 1498, level = 25.0, percentage = 99.8 } },
            ["pvp_rankings_ultra_league"] = new[] { new { pokemon = pokemonId, form, rank = 1, cp = 2498, level = 40.0, percentage = 99.5 } },
            ["template"] = template,
        };
    }

    private static Dictionary<string, object> BuildRaidWebhook(
        JsonElement alarm, double lat, double lon, long endTime, string template)
    {
        var pokemonId = GetInt(alarm, "pokemon_id", 150);
        if (pokemonId is 0 or 9000)
        {
            pokemonId = 150; // "Any boss" → use Mewtwo for test
        }

        var form = GetInt(alarm, "form", 0);
        var level = GetInt(alarm, "level", 5);
        if (level == 9000)
        {
            level = 5;
        }

        return new Dictionary<string, object>
        {
            ["pokemon_id"] = pokemonId,
            ["form"] = form,
            ["latitude"] = lat,
            ["longitude"] = lon,
            ["end"] = endTime,
            ["level"] = level,
            ["team_id"] = GetInt(alarm, "team", 0),
            ["move_1"] = 1,
            ["move_2"] = 1,
            ["cp"] = 50000,
            ["gym_name"] = "Test Gym",
            ["gym_id"] = "test-gym-001",
            ["evolution"] = 0,
            ["is_exclusive"] = false,
            ["template"] = template,
        };
    }

    private static Dictionary<string, object> BuildEggWebhook(
        JsonElement alarm, double lat, double lon, DateTimeOffset now, long endTime, string template)
    {
        var level = GetInt(alarm, "level", 5);
        if (level == 9000)
        {
            level = 5;
        }

        return new Dictionary<string, object>
        {
            ["pokemon_id"] = 0,
            ["latitude"] = lat,
            ["longitude"] = lon,
            ["start"] = now.ToUnixTimeSeconds(),
            ["end"] = endTime,
            ["level"] = level,
            ["team_id"] = GetInt(alarm, "team", 0),
            ["gym_name"] = "Test Gym",
            ["gym_id"] = "test-gym-001",
            ["is_exclusive"] = false,
            ["template"] = template,
        };
    }

    private static Dictionary<string, object> BuildQuestWebhook(
        JsonElement alarm, double lat, double lon, string template)
    {
        var pokemonId = GetInt(alarm, "pokemon_id", 25);
        if (pokemonId == 0)
        {
            pokemonId = 25; // "Any Pokemon reward" → use Pikachu for test
        }

        var rewardType = GetInt(alarm, "reward_type", 7);

        return new Dictionary<string, object>
        {
            ["pokestop_id"] = "test-stop-001",
            ["pokestop_name"] = "Test PokeStop",
            ["latitude"] = lat,
            ["longitude"] = lon,
            ["quest_type"] = 1,
            ["quest_target"] = 3,
            ["quest_reward_type"] = rewardType,
            ["item_id"] = rewardType == 2 ? GetInt(alarm, "reward", 1) : 0,
            ["item_amount"] = 1,
            ["pokemon_id"] = rewardType == 7 ? pokemonId : 0,
            ["template"] = template,
        };
    }

    private static Dictionary<string, object> BuildInvasionWebhook(
        JsonElement alarm, double lat, double lon, string template)
    {
        var gruntType = GetString(alarm, "grunt_type", "41"); // Default: mixed grunt

        return new Dictionary<string, object>
        {
            ["pokestop_id"] = "test-stop-001",
            ["pokestop_name"] = "Test PokeStop",
            ["latitude"] = lat,
            ["longitude"] = lon,
            ["incident_expire_timestamp"] = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds(),
            ["grunt_type"] = gruntType,
            ["template"] = template,
        };
    }

    private static Dictionary<string, object> BuildLureWebhook(
        JsonElement alarm, double lat, double lon, string template)
    {
        var lureId = GetInt(alarm, "lure_id", 501);

        return new Dictionary<string, object>
        {
            ["pokestop_id"] = "test-stop-001",
            ["pokestop_name"] = "Test PokeStop",
            ["latitude"] = lat,
            ["longitude"] = lon,
            ["lure_expiration"] = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds(),
            ["lure_id"] = lureId,
            ["template"] = template,
        };
    }

    private static Dictionary<string, object> BuildNestWebhook(
        JsonElement alarm, double lat, double lon, string template)
    {
        var pokemonId = GetInt(alarm, "pokemon_id", 25);
        if (pokemonId == 0)
        {
            pokemonId = 25; // "Any nest Pokemon" → use Pikachu for test
        }

        return new Dictionary<string, object>
        {
            ["nest_id"] = 1,
            ["pokemon_id"] = pokemonId,
            ["pokemon_form"] = GetInt(alarm, "form", 0),
            ["name"] = "Test Park Nest",
            ["latitude"] = lat,
            ["longitude"] = lon,
            ["pokemon_avg"] = 12.5,
            ["pokemon_count"] = 25,
            ["template"] = template,
        };
    }

    private static Dictionary<string, object> BuildGymWebhook(
        JsonElement alarm, double lat, double lon, string template) => new()
        {
            ["gym_id"] = "test-gym-001",
            ["gym_name"] = "Test Gym",
            ["latitude"] = lat,
            ["longitude"] = lon,
            ["team_id"] = GetInt(alarm, "team", 1),
            ["old_team_id"] = 0,
            ["slots_available"] = 3,
            ["template"] = template,
        };

    private static JsonElement? FindAlarmByUid(JsonElement allAlarms, int uid)
    {
        if (allAlarms.ValueKind == JsonValueKind.Array)
        {
            var matchingAlarm = allAlarms
                .EnumerateArray()
                .FirstOrDefault(item =>
                    item.TryGetProperty("uid", out var uidProp) &&
                    uidProp.GetInt32() == uid);

            if (matchingAlarm.ValueKind != JsonValueKind.Undefined)
            {
                return matchingAlarm;
            }
        }
        return null;
    }

    private static int GetInt(JsonElement element, string property, int defaultValue)
    {
        if (element.TryGetProperty(property, out var prop) &&
            prop.ValueKind == JsonValueKind.Number &&
            prop.TryGetInt32(out var val))
        {
            return val;
        }

        return defaultValue;
    }

    private static string GetString(JsonElement element, string property, string defaultValue)
    {
        if (element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString() ?? defaultValue;
        }
        return defaultValue;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sending test alert: type={AlarmType}, uid={Uid}, user={UserId}")]
    private static partial void LogSendingTestAlert(ILogger logger, string alarmType, int uid, string userId);
}
