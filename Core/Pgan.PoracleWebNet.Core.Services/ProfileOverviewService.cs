using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;

using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public class ProfileOverviewService(
    IPoracleTrackingProxy trackingProxy,
    IPoracleHumanProxy humanProxy,
    IFeatureGate featureGate,
    IPoracleIncidentProxy incidentProxy,
    ILinkedAccount linked,
    IPoracleV2SchemaService? v2Schema = null) : IProfileOverviewService
{
    private static readonly string[] AlarmTypes =
        ["pokemon", "raid", "egg", "quest", "invasion", "lure", "nest", "gym", "maxbattle", "fort"];

    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IPoracleTrackingProxy _trackingProxy = trackingProxy;
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly IPoracleIncidentProxy _incidentProxy = incidentProxy;
    private readonly IPoracleV2SchemaService? _v2Schema = v2Schema;
    private readonly ILinkedAccount _linked = linked;

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

        // Checked before anything is written: this is the one write path that never ran the alarm models' own
        // rules, so a hand-edited backup could persist a distance of -77, an IV window of -999 to 500 or a
        // clean value of 99 -- every one of which the matching POST refuses with a 400. A partial import is
        // worse than a refused one. See #548.
        await this.EnsureAlarmsAreValidAsync(alarms);

        // Every row is prepared before the first write. Translation to each type's v2 rule happens later,
        // inside the proxy, one type per create: a row v2 can't express refuses its own type's write, and the
        // types ahead of it in AlarmTypes are already written by then. The controller deletes the new profile
        // on that refusal, which takes those rules with it, so a refused import leaves nothing behind.
        var (batches, events) = PrepareImport(alarms);

        // One v2 create per type, aimed at the target profile with ?profile=N. The user's active profile is
        // never switched, so there is nothing to restore if a write fails. A rule already on the target
        // exactly, or one that would take another rule over, is left out by PoracleNG and is not counted.
        var totalCreated = 0;
        foreach (var (type, rows) in batches)
        {
            var body = JsonSerializer.SerializeToElement(rows);
            var result = await this._trackingProxy.CreateOnProfileAsync(type, userId, body, targetProfileNo);
            totalCreated += result.Inserts;
        }

        if (events.Count > 0)
        {
            var uids = await IncidentCreateProtocol.CreateAsync(
                this._incidentProxy, userId, events, single: false, profileNo: targetProfileNo);
            totalCreated += uids.Count(uid => uid > 0);
        }

        return totalCreated;
    }

    /// <summary>
    /// The rows to write per type, in <see cref="AlarmTypes"/> order, and the Pokestop events the invasion
    /// array carries.
    /// </summary>
    /// <remarks>
    /// An export folds Pokestop events (kecleon, gold-stop, showcase) into the invasion array, as v1 held
    /// them, and v2's invasion route refuses them; they are written to the incident route instead. A grunt
    /// saved as <c>metal</c>, which v2 also refuses, is written as <c>steel</c>.
    /// </remarks>
    private static (List<(string Type, List<JsonElement> Rows)> Batches, List<PokestopEvent> Events) PrepareImport(JsonElement alarms)
    {
        var batches = new List<(string, List<JsonElement>)>();
        var events = new List<PokestopEvent>();

        foreach (var type in AlarmTypes)
        {
            if (!alarms.TryGetProperty(type, out var alarmsArray) ||
                alarmsArray.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var rows = new List<JsonElement>();
            foreach (var alarm in alarmsArray.EnumerateArray())
            {
                // The file decides none of these. They name the owner and the profile, which are fixed by
                // who is importing and where: a backup carrying profile_no: 7 must not land on profile 7
                // (#411), id names the owner, and a hand-edited backup may still carry a uid. See #465.
                var cleaned = alarm;
                foreach (var owned in ImportIgnoredFields)
                {
                    if (cleaned.TryGetProperty(owned, out _))
                    {
                        cleaned = PoracleJsonHelper.StripProperty(cleaned, owned);
                    }
                }

                if (type == "invasion" && cleaned.ValueKind == JsonValueKind.Object)
                {
                    var invasion = Deserialize<Invasion>(cleaned) ?? new Invasion();
                    if (InvasionService.AsPokestopEvent(invasion) is { } pokestopEvent)
                    {
                        events.Add(pokestopEvent);
                        continue;
                    }

                    cleaned = WithGruntType(cleaned, InvasionGruntTypes.Normalise(invasion.GruntType));
                }

                rows.Add(cleaned);
            }

            if (rows.Count > 0)
            {
                batches.Add((type, rows));
            }
        }

        return (batches, events);
    }

    private static JsonElement WithGruntType(JsonElement row, string? gruntType)
    {
        if (gruntType is null
            || (row.TryGetProperty("grunt_type", out var stored)
                && stored.ValueKind == JsonValueKind.String
                && stored.GetString() == gruntType))
        {
            return row;
        }

        var node = JsonNode.Parse(row.GetRawText())!.AsObject();
        node["grunt_type"] = gruntType;
        return JsonSerializer.SerializeToElement(node);
    }

    /// <summary>
    /// Runs each imported alarm through the same model rules a direct POST would.
    /// </summary>
    /// <remarks>
    /// Deserializing into the *Create model applies its [Range] and [StringLength] attributes; the monster
    /// pair check catches windows nothing can satisfy. A file that fails is refused whole, so the caller
    /// never ends up with half a profile. See #548.
    /// </remarks>
    private async Task EnsureAlarmsAreValidAsync(JsonElement alarms)
    {
        if (alarms.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var carriesPersonal = false;
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
                carriesPersonal |= ValidateAlarm(type, alarm, index);
            }
        }

        // One schema read for the whole file, and only when a rule asks for the bit. Duplicate needs no
        // such check: it copies rules the same server already holds.
        // No schema service means no capabilities, which refuses.
        if (carriesPersonal && (this._v2Schema is null || !(await this._v2Schema.GetAsync()).PersonalTracking))
        {
            throw new AlarmValidationException(MonsterService.PersonalUnsupportedMessage);
        }
    }

    /// <summary>Validates one imported alarm; true when it is a pokemon rule with a personal bit set.</summary>
    private static bool ValidateAlarm(string type, JsonElement alarm, int index)
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
            return false;
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

            return monster.Personal != 0;
        }

        return false;
    }

    private static T? Deserialize<T>(JsonElement alarm) =>
        JsonSerializer.Deserialize<T>(alarm.GetRawText(), SnakeCaseOptions);

    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
    public async Task<JsonElement> GetAllProfilesOverviewAsync(string userId)
    {
        var pair = await this._linked.ResolveAsync(userId);
        if (!pair.IsLinked)
        {
            return await this._trackingProxy.GetAllTrackingAllProfilesAsync(userId);
        }

        // A linked person sees both humans' rules in one snapshot, each row stamped with the side it lives
        // on. The caller's own snapshot is the base, so any member that is not a list of rules (the profile
        // list, status) stays theirs.
        JsonObject? merged = null;
        foreach (var humanId in pair.ExistingHumanIds)
        {
            var snapshot = await this._trackingProxy.GetAllTrackingAllProfilesAsync(humanId);
            if (JsonNode.Parse(snapshot.GetRawText()) is not JsonObject obj)
            {
                continue;
            }

            var side = pair.SideOf(humanId);
            foreach (var (key, value) in obj)
            {
                // Only the tracking types hold rules. The snapshot's "profile" array is the profile list,
                // which is not an alarm and numbers its entries per human, so it is left as it came.
                if (AlarmTypes.Contains(key) && value is JsonArray rows)
                {
                    foreach (var row in rows.OfType<JsonObject>())
                    {
                        if (side is { } s)
                        {
                            row["destination"] = AccountLinkKeys.Wire(s);
                        }
                    }
                }
            }

            if (merged is null)
            {
                merged = obj;
                continue;
            }

            foreach (var (key, value) in obj.ToList())
            {
                if (!AlarmTypes.Contains(key) || value is not JsonArray rows)
                {
                    continue;
                }

                if (merged[key] is JsonArray existing)
                {
                    foreach (var row in rows.ToList())
                    {
                        rows.Remove(row);
                        existing.Add(row);
                    }
                }
                else
                {
                    obj.Remove(key);
                    merged[key] = rows;
                }
            }
        }

        return merged is null
            ? await this._trackingProxy.GetAllTrackingAllProfilesAsync(userId)
            : JsonSerializer.SerializeToElement(merged);
    }

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
