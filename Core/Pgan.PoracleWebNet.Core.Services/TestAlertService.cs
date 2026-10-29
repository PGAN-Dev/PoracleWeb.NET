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
    ILinkedAccount linked,
    ILogger<TestAlertService> logger) : ITestAlertService
{
    // NYC fallback when the user's profile has no coordinates set (0,0 is not a valid spawn site).
    private const double FallbackLatitude = 40.7128;
    private const double FallbackLongitude = -74.006;
    private const double CoordinateEpsilon = 1e-6;

    /// <summary>
    /// Posted to the target after PoracleNG accepts a test, so the DM cannot pass for a live alert (#1097).
    /// PoracleNG renders the alert through the member's own template, so the alert itself cannot be labelled.
    /// Render and delivery order is not guaranteed, so it never says "above" or "below". Plain ASCII with no
    /// Markdown characters: Telegram parses text as Markdown by default. English only, as nothing in the
    /// backend localises messages.
    /// </summary>
    public const string TestNote =
        "Test notification from PoracleWeb.NET: the alert sent with this note is a sample built from your alarm's filters at your saved location. " +
        "It isn't a live alert, so there's nothing to go and find.";

    private static readonly HashSet<string> ValidTypes =
        ["pokemon", "raid", "egg", "quest", "invasion", "lure", "nest", "gym"];

    private readonly IPoracleApiProxy _apiProxy = apiProxy;
    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly ILinkedAccount _linked = linked;
    private readonly ILogger<TestAlertService> _logger = logger;
    private readonly IPoracleTrackingProxy _trackingProxy = trackingProxy;
    private readonly IReadOnlyList<ITestPayloadBuilder> _payloadBuilders = [.. payloadBuilders];

    public async Task SendTestAlertAsync(string userId, string alarmType, int uid)
    {
        if (!ValidTypes.Contains(alarmType))
        {
            throw new ArgumentException($"Invalid alarm type: {alarmType}", nameof(alarmType));
        }

        // The alarm may live on either human of a linked pair; the alert is sent as the human that owns it,
        // so a rule on Telegram goes to Telegram. An unlinked caller has one id and makes one lookup.
        var pair = await this._linked.ResolveAsync(userId);
        var candidates = pair.ExistingHumanIds;
        string ownerId = userId;
        JsonElement human = default;
        JsonElement? alarm = null;
        for (var i = 0; i < candidates.Count; i++)
        {
            ownerId = candidates[i];

            // Fetch alarm data and human data in parallel — both trips to PoracleNG.
            var alarmTask = this._trackingProxy.GetByUserAsync(alarmType, ownerId);
            var humanTask = this._humanProxy.GetHumanAsync(ownerId);
            await Task.WhenAll(alarmTask, humanTask);

            alarm = FindAlarmByUid(alarmTask.Result, uid);
            if (alarm is null && candidates.Count > 1)
            {
                // A linked pair: a uid on neither human is a 404, whatever the last human's record says.
                if (i < candidates.Count - 1)
                {
                    continue;
                }

                throw new KeyNotFoundException($"Alarm with uid {uid} not found");
            }

            human = humanTask.Result
                ?? throw new InvalidOperationException("User not found");
            break;
        }

        var found = alarm ?? throw new KeyNotFoundException($"Alarm with uid {uid} not found");

        var target = BuildTarget(ownerId, human);
        // The alarm's DTS template id lives at the envelope level — PoracleNG picks the DTS
        // entry by matching (target.template, alarmType). Some alarm rows store it as an int.
        if (found.TryGetProperty("template", out var tmpl))
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
            alarmType, found, latitude, longitude, DateTimeOffset.UtcNow));

        var request = new TestAlertRequest
        {
            Type = build.WireType,
            Target = target,
            Webhook = build.Webhook,
        };

        LogSendingTestAlert(this._logger, alarmType, build.WireType, uid, LogSafe.Id(ownerId));
        await this._apiProxy.SendTestAlertAsync(request);

        // Only reached once PoracleNG accepted the test: a refusal above throws and sends no note.
        await this.SendTestNoteAsync(alarmType, uid, target);
    }

    /// <summary>
    /// Tells the target that the alert delivered with this note is a test. The alert is already on its way and
    /// the note is only a label for it, so a failure here is logged and swallowed rather than reported as a
    /// failed test. The target is not logged: for a webhook it is a URL carrying a token.
    /// </summary>
    private async Task SendTestNoteAsync(string alarmType, int uid, TestAlertTarget target)
    {
        try
        {
            await this._apiProxy.DeliverTextAsync(target.Id, target.Type, TestNote);
        }
        catch (Exception ex)
        {
            LogTestNoteFailed(this._logger, ex, alarmType, uid, target.Type);
        }
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Test alert was sent but its test note was not: alarmType={AlarmType}, uid={Uid}, targetType={TargetType}")]
    private static partial void LogTestNoteFailed(ILogger logger, Exception exception, string alarmType, int uid, string targetType);
}
