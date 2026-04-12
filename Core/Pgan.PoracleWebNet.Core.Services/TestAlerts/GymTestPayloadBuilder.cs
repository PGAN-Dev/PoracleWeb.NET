using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services.TestAlerts;

/// <summary>
/// Builds a legacy team-change gym webhook — our live PoracleNG instance ships a DTS template
/// of <c>type: "gym"</c> (not <c>fort-update</c>) that reads flat <c>team_id</c>/<c>old_team_id</c>/
/// <c>slots_available</c> fields. Matches the alarm filter's requested <c>team</c> on the
/// "new" side and flips to a different team on the "old" side so the DM renders a visible
/// transition (Poracle enriches <c>previousControlName</c>/<c>teamName</c> from these).
/// </summary>
public sealed class GymTestPayloadBuilder : ITestPayloadBuilder
{
    public bool CanBuild(string alarmType) => alarmType == "gym";

    public Task<TestPayloadBuildResult> BuildAsync(TestPayloadContext context)
    {
        var teamFilter = context.Alarm.GetInt("team", 4);
        var newTeam = teamFilter is >= 0 and <= 3 ? teamFilter : 2; // Default Mystic if "any".
        var oldTeam = newTeam == 1 ? 2 : 1; // Flip to a different team so the transition renders.

        var webhook = new Dictionary<string, object>
        {
            ["gym_id"] = "test-gym-001",
            ["name"] = "Test Gym",
            ["url"] = string.Empty,
            ["latitude"] = context.Latitude,
            ["longitude"] = context.Longitude,
            ["team_id"] = newTeam,
            ["old_team_id"] = oldTeam,
            ["slots_available"] = 3,
            ["old_slots_available"] = 6,
            ["in_battle"] = false,
            ["raid_active_until"] = 0,
        };

        return Task.FromResult(new TestPayloadBuildResult("gym", webhook));
    }
}
