using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services.TestAlerts;

/// <summary>
/// Handles both <c>invasion</c> and <c>lure</c> alarms — both render off the wire
/// <c>pokestop</c> event. PoracleNG's pokestop controller branches to the invasion or lure
/// DTS template based on which fields are present in the webhook.
/// </summary>
public sealed class PokestopTestPayloadBuilder : ITestPayloadBuilder
{
    public bool CanBuild(string alarmType) => alarmType is "invasion" or "lure";

    public Task<TestPayloadBuildResult> BuildAsync(TestPayloadContext context)
    {
        var now = context.Now.ToUnixTimeSeconds();
        var expire = context.Now.AddMinutes(30).ToUnixTimeSeconds();

        var webhook = new Dictionary<string, object>
        {
            ["name"] = "Test Pokéstop",
            ["pokestop_id"] = "test-pokestop-001",
            ["latitude"] = context.Latitude,
            ["longitude"] = context.Longitude,
            ["updated"] = now,
            ["last_modified"] = now,
            ["url"] = string.Empty,
        };

        if (context.AlarmType == "invasion")
        {
            var gruntType = context.Alarm.GetInt("grunt_type", 0);
            if (gruntType <= 0)
            {
                // Filter uses a string key (e.g. "male_1") — fall back to a common grunt id.
                gruntType = 41;
            }

            webhook["incident_start"] = now;
            webhook["incident_expiration"] = expire;
            webhook["incident_grunt_type"] = gruntType;
        }
        else // lure
        {
            var lureId = context.Alarm.GetInt("lure_id", 501);
            if (lureId <= 0)
            {
                lureId = 501;
            }

            webhook["lure_expiration"] = expire;
            webhook["lure_id"] = lureId;
        }

        return Task.FromResult(new TestPayloadBuildResult("pokestop", webhook));
    }
}
