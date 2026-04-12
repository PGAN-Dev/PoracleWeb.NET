using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public partial class TestAlertService(
    IPoracleApiProxy apiProxy,
    IPoracleTrackingProxy trackingProxy,
    IPoracleHumanProxy humanProxy,
    IEnumerable<ITestPayloadBuilder> payloadBuilders,
    ILogger<TestAlertService> logger) : ITestAlertService
{
    // NYC fallback when the user's profile has no coordinates set (0,0 is not a valid spawn site).
    private const double FallbackLatitude = 40.7128;
    private const double FallbackLongitude = -74.006;
    private const double CoordinateEpsilon = 1e-6;

    private static readonly HashSet<string> ValidTypes =
        ["pokemon", "raid", "egg", "quest", "invasion", "lure", "nest", "gym"];

    private readonly IPoracleApiProxy _apiProxy = apiProxy;
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly ILogger<TestAlertService> _logger = logger;
    private readonly IPoracleTrackingProxy _trackingProxy = trackingProxy;
    private readonly IReadOnlyList<ITestPayloadBuilder> _payloadBuilders = [.. payloadBuilders];

    public async Task SendTestAlertAsync(string userId, string alarmType, int uid)
    {
        if (!ValidTypes.Contains(alarmType))
        {
            throw new ArgumentException($"Invalid alarm type: {alarmType}", nameof(alarmType));
        }

        // Fetch alarm data and human data in parallel — both trips to PoracleNG.
        var alarmTask = this._trackingProxy.GetByUserAsync(alarmType, userId);
        var humanTask = this._humanProxy.GetHumanAsync(userId);
        await Task.WhenAll(alarmTask, humanTask);

        var allAlarms = alarmTask.Result;
        var human = humanTask.Result
            ?? throw new InvalidOperationException("User not found");

        var alarm = FindAlarmByUid(allAlarms, uid)
            ?? throw new KeyNotFoundException($"Alarm with uid {uid} not found");

        var target = BuildTarget(userId, human);
        // The alarm's DTS template id lives at the envelope level — PoracleNG picks the DTS
        // entry by matching (target.template, alarmType). Some alarm rows store it as an int.
        if (alarm.TryGetProperty("template", out var tmpl))
        {
#pragma warning disable IDE0072
            target.Template = tmpl.ValueKind switch
            {
                JsonValueKind.String => tmpl.GetString() ?? "1",
                JsonValueKind.Number when tmpl.TryGetInt32(out var n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => "1",
            };
#pragma warning restore IDE0072
        }
        else
        {
            target.Template = "1";
        }

        var builder = this._payloadBuilders.FirstOrDefault(b => b.CanBuild(alarmType))
            ?? throw new InvalidOperationException($"No test payload builder registered for alarm type '{alarmType}'.");

        var latitude = Math.Abs(target.Latitude) > CoordinateEpsilon ? target.Latitude : FallbackLatitude;
        var longitude = Math.Abs(target.Longitude) > CoordinateEpsilon ? target.Longitude : FallbackLongitude;

        var build = await builder.BuildAsync(new TestPayloadContext(
            alarmType, alarm, latitude, longitude, DateTimeOffset.UtcNow));

        var request = new TestAlertRequest
        {
            Type = build.WireType,
            Target = target,
            Webhook = build.Webhook,
        };

        LogSendingTestAlert(this._logger, alarmType, build.WireType, uid, userId);
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

    private static JsonElement? FindAlarmByUid(JsonElement allAlarms, int uid)
    {
        if (allAlarms.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in allAlarms.EnumerateArray())
        {
            if (item.TryGetProperty("uid", out var uidProp)
                && uidProp.ValueKind == JsonValueKind.Number
                && uidProp.TryGetInt32(out var rowUid)
                && rowUid == uid)
            {
                return item;
            }
        }

        return null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sending test alert: alarmType={AlarmType}, wireType={WireType}, uid={Uid}, user={UserId}")]
    private static partial void LogSendingTestAlert(ILogger logger, string alarmType, string wireType, int uid, string userId);
}
