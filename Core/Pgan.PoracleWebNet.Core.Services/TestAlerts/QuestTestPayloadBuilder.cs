using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services.TestAlerts;

/// <summary>
/// Builds a quest webhook whose reward shape matches the alarm's <c>reward_type</c>.
/// The quest webhook has a native <c>template</c> field for the in-game quest template key
/// (e.g. <c>CHALLENGE_BASE_SPIN_S_ITEM</c>) — the DTS template id lives on the outer
/// <c>target.template</c> envelope field, not here. We set a stable placeholder for the
/// in-game template so the DM renders a realistic task string.
/// </summary>
public sealed class QuestTestPayloadBuilder : ITestPayloadBuilder
{
    private const int DefaultRewardPokemonId = 25;
    private const int StardustRewardType = 3;
    private const int ItemRewardType = 2;
    private const int CandyRewardType = 4;
    private const int PokemonRewardType = 7;
    private const int MegaEnergyRewardType = 12;

    public bool CanBuild(string alarmType) => alarmType == "quest";

    public Task<TestPayloadBuildResult> BuildAsync(TestPayloadContext context)
    {
        var rewardType = context.Alarm.GetInt("reward_type", PokemonRewardType);
        var reward = context.Alarm.GetInt("reward", 0);
        var form = context.Alarm.GetInt("form", 0);
        var shiny = context.Alarm.GetInt("shiny", 0) == 1;

        var rewardInfo = new Dictionary<string, object>();
        switch (rewardType)
        {
            case ItemRewardType:
                rewardInfo["item_id"] = reward > 0 ? reward : 1;
                rewardInfo["amount"] = 3;
                break;

            case StardustRewardType:
                rewardInfo["amount"] = reward > 0 ? reward : 500;
                break;

            case CandyRewardType:
                rewardInfo["pokemon_id"] = reward > 0 ? reward : DefaultRewardPokemonId;
                rewardInfo["amount"] = 3;
                break;

            case PokemonRewardType:
                rewardInfo["pokemon_id"] = reward > 0 ? reward : DefaultRewardPokemonId;
                rewardInfo["form_id"] = form;
                rewardInfo["costume_id"] = 0;
                rewardInfo["shiny"] = shiny ? 1 : 0;
                break;

            case MegaEnergyRewardType:
                rewardInfo["pokemon_id"] = reward > 0 ? reward : DefaultRewardPokemonId;
                rewardInfo["amount"] = 25;
                break;

            default:
                rewardInfo["pokemon_id"] = DefaultRewardPokemonId;
                break;
        }

        var webhook = new Dictionary<string, object>
        {
            ["pokestop_id"] = "test-pokestop-001",
            ["pokestop_name"] = "Test Pokéstop",
            ["pokestop_url"] = string.Empty,
            ["latitude"] = context.Latitude,
            ["longitude"] = context.Longitude,
            ["type"] = 4,
            ["target"] = 3,
            ["template"] = "CHALLENGE_BASE_SPIN_S_ITEM",
            ["conditions"] = Array.Empty<object>(),
            ["rewards"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["info"] = rewardInfo,
                    ["type"] = rewardType,
                },
            },
            ["updated"] = context.Now.ToUnixTimeSeconds(),
            ["quest_task"] = "Catch 3 Pokémon",
            ["with_ar"] = true,
        };

        return Task.FromResult(new TestPayloadBuildResult("quest", webhook));
    }
}
