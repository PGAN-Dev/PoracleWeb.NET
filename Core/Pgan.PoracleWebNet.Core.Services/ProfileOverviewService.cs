using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public class ProfileOverviewService(
    IPoracleTrackingProxy trackingProxy,
    IPoracleHumanProxy humanProxy,
    IFeatureGate featureGate) : IProfileOverviewService
{
    private static readonly string[] AlarmTypes =
        ["pokemon", "raid", "egg", "quest", "invasion", "lure", "nest", "gym", "maxbattle", "fort"];

    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IPoracleTrackingProxy _trackingProxy = trackingProxy;
    private readonly IFeatureGate _featureGate = featureGate;

    public async Task<int> DuplicateProfileAsync(string userId, int sourceProfileNo, int newProfileNo)
    {
        // Get all alarms across all profiles
        var allTracking = await this._trackingProxy.GetAllTrackingAllProfilesAsync(userId);

        // Pre-validate: this service writes to the tracking proxy directly, bypassing the per-type
        // alarm services and their FeatureGate checks. Without this pass, a partial duplicate could
        // succeed for some types and 403 mid-loop for a disabled type — leaving the new profile
        // half-populated. Walk the source first to surface the disable error before any writes. (#236)
        await this.EnsureNoDisabledTypesAsync(allTracking, alarm => alarm.GetIntProp("profile_no") == sourceProfileNo);

        // Remember current profile so we can restore it
        var humanJson = await this._humanProxy.GetHumanAsync(userId);
        var originalProfileNo = humanJson?.GetIntProp("current_profile_no") ?? 1;

        // Switch to the new profile so PoracleNG scopes creates to it
        await this._humanProxy.SwitchProfileAsync(userId, newProfileNo);

        var totalCreated = 0;
        try
        {
            foreach (var type in AlarmTypes)
            {
                if (!allTracking.TryGetProperty(type, out var alarmsArray) ||
                    alarmsArray.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var alarm in alarmsArray.EnumerateArray())
                {
                    if (alarm.GetIntProp("profile_no") != sourceProfileNo)
                    {
                        continue;
                    }

                    // Strip uid so PoracleNG creates a new alarm instead of updating
                    var cleaned = PoracleJsonHelper.StripProperty(alarm, "uid");
                    await this._trackingProxy.CreateAsync(type, userId, cleaned);
                    totalCreated++;
                }
            }
        }
        finally
        {
            // Always restore the original profile
            await this._humanProxy.SwitchProfileAsync(userId, originalProfileNo);
        }

        return totalCreated;
    }

    /// <summary>
    /// Fields an imported alarm may not dictate: they identify the owner and the profile, which are
    /// determined by who is importing and where. See #465.
    /// </summary>
    private static readonly string[] ImportIgnoredFields = ["uid", "profile_no", "id"];

    public async Task<int> ImportAlarmsAsync(string userId, int targetProfileNo, JsonElement alarms)
    {
        // Pre-validate the import payload before any state mutation. See DuplicateProfileAsync above
        // for why this can't be a per-iteration check. (#236)
        await this.EnsureNoDisabledTypesAsync(alarms, _ => true);

        var humanJson = await this._humanProxy.GetHumanAsync(userId);
        var originalProfileNo = humanJson?.GetIntProp("current_profile_no") ?? 1;

        // Checked before anything is written, and before the profile switch: this is the one write path
        // that never ran the alarm models' own rules, so a hand-edited backup could persist a distance of
        // -77, an IV window of -999 to 500 or a clean value of 99 -- every one of which the matching POST
        // refuses with a 400. A partial import is worse than a refused one. See #548.
        EnsureAlarmsAreValid(alarms);

        await this._humanProxy.SwitchProfileAsync(userId, targetProfileNo);

        var totalCreated = 0;
        try
        {
            foreach (var type in AlarmTypes)
            {
                if (!alarms.TryGetProperty(type, out var alarmsArray) ||
                    alarmsArray.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var alarm in alarmsArray.EnumerateArray())
                {
                    // The file decides none of these. PoracleNG takes a submitted profile_no at face
                    // value (#411), so an alarm carrying profile_no: 7 landed on profile 7 instead of
                    // the profile just created -- an orphan that a future profile 7 would inherit. id
                    // is stripped for the same reason: it names the owner, and the URL already does
                    // that. uid is stripped because a hand-edited backup may still carry one. See #465.
                    var cleaned = alarm;
                    foreach (var owned in ImportIgnoredFields)
                    {
                        if (cleaned.TryGetProperty(owned, out _))
                        {
                            cleaned = PoracleJsonHelper.StripProperty(cleaned, owned);
                        }
                    }
                    await this._trackingProxy.CreateAsync(type, userId, cleaned);
                    totalCreated++;
                }
            }
        }
        finally
        {
            await this._humanProxy.SwitchProfileAsync(userId, originalProfileNo);
        }

        return totalCreated;
    }


    /// <summary>
    /// Runs each imported alarm through the same model rules a direct POST would.
    /// </summary>
    /// <remarks>
    /// Deserializing into the *Create model applies its [Range] and [StringLength] attributes; the monster
    /// pair check catches windows nothing can satisfy. A file that fails is refused whole, so the caller
    /// never ends up with half a profile. See #548.
    /// </remarks>
    private static void EnsureAlarmsAreValid(JsonElement alarms)
    {
        if (alarms.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var type in AlarmTypes)
        {
            if (!alarms.TryGetProperty(type, out var alarmsArray)
                || alarmsArray.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var index = 0;
            foreach (var alarm in alarmsArray.EnumerateArray())
            {
                index++;
                ValidateAlarm(type, alarm, index);
            }
        }
    }

    private static void ValidateAlarm(string type, JsonElement alarm, int index)
    {
        object? model;
        try
        {
            model = type switch
            {
                "pokemon" => Deserialize<MonsterCreate>(alarm),
                "raid" => Deserialize<RaidCreate>(alarm),
                "egg" => Deserialize<EggCreate>(alarm),
                "quest" => Deserialize<QuestCreate>(alarm),
                "invasion" => Deserialize<InvasionCreate>(alarm),
                "lure" => Deserialize<LureCreate>(alarm),
                "nest" => Deserialize<NestCreate>(alarm),
                "gym" => Deserialize<GymCreate>(alarm),
                "fort" => Deserialize<FortChangeCreate>(alarm),
                "maxbattle" => Deserialize<MaxBattleCreate>(alarm),
                _ => null,
            };
        }
        catch (JsonException ex)
        {
            throw new AlarmValidationException(
                $"{type} alarm {index} in this file could not be read: {ex.Message}");
        }

        if (model is null)
        {
            return;
        }

        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true))
        {
            throw new AlarmValidationException(
                $"{type} alarm {index} in this file is not valid: {results[0].ErrorMessage}");
        }

        // The [Range] and [StringLength] attributes live on the *Create DTOs -- the domain models carry
        // none, so validating those found nothing and the import sailed through. The cross-field check
        // wants the domain model, so the same JSON is read a second time; Core.Services does not
        // reference Core.Mappings and one import is not worth a new project dependency. See #548.
        if (model is MonsterCreate)
        {
            var monster = Deserialize<Monster>(alarm) ?? new Monster();
            var inverted = MonsterRangeValidator.Validate(monster);
            if (inverted is not null)
            {
                throw new AlarmValidationException($"{type} alarm {index} in this file is impossible: {inverted}");
            }
        }
    }

    private static T? Deserialize<T>(JsonElement alarm) =>
        JsonSerializer.Deserialize<T>(alarm.GetRawText(), SnakeCaseOptions);

    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
    public async Task<JsonElement> GetAllProfilesOverviewAsync(string userId) => await this._trackingProxy.GetAllTrackingAllProfilesAsync(userId);

    /// <summary>
    /// Throws <see cref="FeatureDisabledException"/> on the first alarm-type bucket that has at
    /// least one matching alarm AND whose <c>disable_*</c> setting is true. <paramref name="alarmFilter"/>
    /// lets <c>DuplicateProfileAsync</c> skip alarms outside the source profile so a disabled type
    /// only blocks when alarms of that type are actually about to be copied.
    /// </summary>
    private async Task EnsureNoDisabledTypesAsync(JsonElement payload, Func<JsonElement, bool> alarmFilter)
    {
        foreach (var type in AlarmTypes)
        {
            if (!payload.TryGetProperty(type, out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var hasAny = false;
            foreach (var alarm in arr.EnumerateArray())
            {
                if (alarmFilter(alarm))
                {
                    hasAny = true;
                    break;
                }
            }

            if (!hasAny)
            {
                continue;
            }

            if (DisableFeatureKeys.ByTrackingType.TryGetValue(type, out var key))
            {
                await this._featureGate.EnsureEnabledAsync(key);
            }
        }
    }
}
