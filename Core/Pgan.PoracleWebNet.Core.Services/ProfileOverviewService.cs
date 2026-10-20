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

        // Pre-validate: PoracleNG's copy knows nothing of this site's disable_* switches, so this pass is
        // the only thing standing between a disabled type and a duplicate that carries it. Walk the source
        // first and refuse before anything is written. (#236)
        await this.EnsureNoDisabledTypesAsync(allTracking, alarm => alarm.GetIntProp("profile_no") == sourceProfileNo);

        // PoracleNG's own copy, rather than re-creating each rule here through the v1 create. Checked
        // against the 5.3.0 bed, which runs production's build: it copied every type including Pokestop
        // events, with ping, clean bits, override_areas and override_location_label intact -- where the
        // re-create blanked every ping (v1 create stores none) and ran each rule through PoracleNG's merge
        // diff. It also needs no profile switch, so the user's active profile is never moved mid-copy. The
        // copy replaces the destination's rules; the destination here is a profile created moments ago, so
        // it has none. /api/profiles/duplicate has always copied this way. See #993.
        await this._humanProxy.CopyProfileAsync(userId, sourceProfileNo, newProfileNo);

        return CountAlarms(allTracking, sourceProfileNo);
    }

    /// <summary>How many of the alarm types this page lists the profile holds.</summary>
    private static int CountAlarms(JsonElement allTracking, int profileNo)
    {
        var count = 0;

        foreach (var type in AlarmTypes)
        {
            if (allTracking.TryGetProperty(type, out var alarms) && alarms.ValueKind == JsonValueKind.Array)
            {
                count += alarms.EnumerateArray().Count(alarm => alarm.GetIntProp("profile_no") == profileNo);
            }
        }

        return count;
    }

    /// <summary>
    /// Fields an imported alarm may not dictate: they identify the owner and the profile, which are
    /// determined by who is importing and where. See #465.
    /// </summary>
    private static readonly string[] ImportIgnoredFields = ["uid", "profile_no", "id"];

    public async Task<int> ImportAlarmsAsync(string userId, int targetProfileNo, JsonElement alarms)
    {
        // Pre-validate the import payload before any state mutation. A per-iteration check could let some
        // types through and 403 mid-loop on a disabled one, leaving the new profile half-populated. (#236)
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
