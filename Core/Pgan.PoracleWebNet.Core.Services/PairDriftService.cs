using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Models.Helpers;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Compares the two humans of a linked pair and, when the person picks a side, makes the other one match.
/// Resolve writes the kept value to the other human only, straight through the proxy: the paired writer
/// would write both and so re-write the side being kept.
/// </summary>
public sealed class PairDriftService(IPoracleHumanProxy human, IPoracleTrackingProxy tracking) : IPairDriftService
{
    private const string NoSchedule = "[]";

    private readonly IPoracleHumanProxy _human = human;
    private readonly IPoracleTrackingProxy _tracking = tracking;

    public async Task<IReadOnlyList<DriftItem>> GetAsync(LinkedPair pair)
    {
        if (!BothExist(pair))
        {
            return [];
        }

        var discord = await this.ReadAsync(pair.Discord!.HumanId);
        var telegram = await this.ReadAsync(pair.Telegram!.HumanId);
        return Compare(discord, telegram);
    }

    public async Task ResolveAsync(LinkedPair pair, DriftField field, LinkSide keep, string expected)
    {
        if (!BothExist(pair))
        {
            throw new DriftChangedException();
        }

        var discord = await this.ReadAsync(pair.Discord!.HumanId);
        var telegram = await this.ReadAsync(pair.Telegram!.HumanId);

        // Judged on a fresh read: the banner may be minutes old and a bot command may have moved things since.
        var item = Compare(discord, telegram).FirstOrDefault(i => i.Field == field);
        if (item is null || !string.Equals(item.Fingerprint, expected, StringComparison.Ordinal))
        {
            throw new DriftChangedException();
        }

        var kept = keep == LinkSide.Discord ? discord : telegram;
        var other = keep == LinkSide.Discord ? telegram : discord;
        var otherId = keep == LinkSide.Discord ? pair.Telegram!.HumanId : pair.Discord!.HumanId;

        switch (field)
        {
            case DriftField.Location:
                await this._human.SetLocationAsync(otherId, kept.Lat, kept.Lon);
                break;
            case DriftField.Areas:
                await this._human.SetAreasTrustedAsync(otherId, kept.Areas);
                break;
            case DriftField.CurrentProfile:
                await this._human.SwitchProfileAsync(otherId, kept.Current);
                break;
            case DriftField.Profiles:
                await this.ResolveProfilesAsync(otherId, kept, other);
                break;
            case DriftField.Places:
                await this.ResolvePlacesAsync(otherId, kept, other);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field));
        }
    }

    private static bool BothExist(LinkedPair pair) =>
        pair.IsLinked && pair.Discord is { Exists: true } && pair.Telegram is { Exists: true };

    private static List<DriftItem> Compare(Snapshot discord, Snapshot telegram)
    {
        var items = new List<DriftItem>();
        Add(items, DriftField.Location, LocationOf(discord), LocationOf(telegram));
        Add(items, DriftField.Areas, AreasOf(discord.Areas), AreasOf(telegram.Areas));
        Add(items, DriftField.CurrentProfile, JsonSerializer.SerializeToElement(discord.Current), JsonSerializer.SerializeToElement(telegram.Current));
        Add(items, DriftField.Profiles, ProfilesOf(discord), ProfilesOf(telegram));
        Add(items, DriftField.Places, PlacesOf(discord), PlacesOf(telegram));
        return items;
    }

    private static void Add(List<DriftItem> items, DriftField field, JsonElement discord, JsonElement telegram)
    {
        var d = discord.GetRawText();
        var t = telegram.GetRawText();
        if (string.Equals(d, t, StringComparison.Ordinal))
        {
            return;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(d + "\n" + t));
        items.Add(new DriftItem(field, discord, telegram, Convert.ToHexStringLower(hash)));
    }

    private static double Round(double value) => Math.Round(value, 5, MidpointRounding.AwayFromZero);

    private static JsonElement LocationOf(Snapshot s) => JsonSerializer.SerializeToElement(new { lat = Round(s.Lat), lon = Round(s.Lon) });

    private static string Key(string name) => UserAreaPolicy.Key(name);

    private static string[] Keys(IEnumerable<string> areas) =>
        [.. areas.Select(Key).Where(k => k.Length > 0).Distinct().Order(StringComparer.Ordinal)];

    private static JsonElement AreasOf(IEnumerable<string> areas) => JsonSerializer.SerializeToElement(Keys(areas));

    private static string[] AreaKeys(string? json) => Keys(AreaListJson.Parse(json));

    private static JsonElement ProfilesOf(Snapshot s) => JsonSerializer.SerializeToElement(
        s.Profiles.OrderBy(p => p.No).Select(p => new
        {
            no = p.No,
            name = p.Name ?? string.Empty,
            active_hours = Hours(p.ActiveHours),
            areas = AreaKeys(p.Area),
            lat = Round(p.Latitude),
            lon = Round(p.Longitude),
        }).ToArray());

    private static JsonElement PlacesOf(Snapshot s) => JsonSerializer.SerializeToElement(
        s.Places.OrderBy(p => p.Label, StringComparer.Ordinal)
            .Select(p => new { label = p.Label, lat = Round(p.Latitude), lon = Round(p.Longitude) }).ToArray());

    /// <summary>PoracleNG stores hours and mins as numbers or as strings; both read as ints, sorted.</summary>
    private static object[] Hours(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        var rows = new List<(int Day, int Hours, int Mins)>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            foreach (var entry in doc.RootElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object))
            {
                rows.Add((Int(entry, "day"), Int(entry, "hours"), Int(entry, "mins")));
            }
        }
        catch (JsonException)
        {
            return [];
        }

        return [.. rows.OrderBy(r => r.Day).ThenBy(r => r.Hours).ThenBy(r => r.Mins).Select(r => (object)new { day = r.Day, hours = r.Hours, mins = r.Mins })];
    }

    private static bool SameSchedule(string? a, string? b) =>
        string.Equals(JsonSerializer.Serialize(Hours(a)), JsonSerializer.Serialize(Hours(b)), StringComparison.Ordinal);

    private static int Int(JsonElement entry, string name)
    {
        if (!entry.TryGetProperty(name, out var v))
        {
            return 0;
        }

        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n))
        {
            return n;
        }

        return v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : 0;
    }

    private async Task<Snapshot> ReadAsync(string humanId)
    {
        var json = await this._human.GetHumanAsync(humanId)
            ?? throw new PoracleRequestRefusedException("A linked account could not be read from PoracleNG.");
        var areas = await this._human.GetAreaListAsync(humanId);
        var profilesJson = await this._human.GetProfilesAsync(humanId);
        var places = await this._human.GetPlacesAsync(humanId);

        var array = profilesJson.ValueKind == JsonValueKind.Array ? profilesJson
            : profilesJson.ValueKind == JsonValueKind.Object && profilesJson.TryGetProperty("profile", out var p) ? p
            : default;
        var profiles = new List<ProfileRow>();
        if (array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                profiles.Add(new ProfileRow(
                    item.GetIntProp("profile_no"),
                    item.GetStringPropOrNull("name"),
                    item.GetStringPropOrNull("area"),
                    item.GetDoubleProp("latitude"),
                    item.GetDoubleProp("longitude"),
                    item.GetStringPropOrNull("active_hours")));
            }
        }

        return new Snapshot(
            json.GetDoubleProp("latitude"), json.GetDoubleProp("longitude"), json.GetIntProp("current_profile_no"),
            [.. areas], profiles, places.Named);
    }

    private async Task ResolveProfilesAsync(string otherId, Snapshot kept, Snapshot other)
    {
        var otherByNo = other.Profiles.ToDictionary(p => p.No);
        var keptNumbers = kept.Profiles.Select(p => p.No).ToHashSet();

        // Refuse before changing anything: a profile that holds alerts is never deleted for the sake of a match.
        var extras = other.Profiles.Where(p => !keptNumbers.Contains(p.No)).OrderBy(p => p.No).ToList();
        if (extras.Count > 0)
        {
            var counts = await this.CountRulesAsync(otherId);
            foreach (var extra in extras)
            {
                if (counts.TryGetValue(extra.No, out var count) && count > 0)
                {
                    throw new DriftProfileHoldsAlarmsException(extra.No, extra.Name, count);
                }
            }
        }

        // Create before delete, ascending, so each new profile is checked against the number it must land on.
        foreach (var row in kept.Profiles.Where(p => !otherByNo.ContainsKey(p.No)).OrderBy(p => p.No))
        {
            var assigned = await this._human.AddProfileAsync(otherId, JsonSerializer.SerializeToElement(new
            {
                name = row.Name,
                active_hours = string.IsNullOrWhiteSpace(row.ActiveHours) ? NoSchedule : row.ActiveHours,
            }));
            if (assigned != row.No)
            {
                await this._human.DeleteProfileAsync(otherId, assigned);
                throw new PoracleRequestRefusedException(
                    $"The other account's profile would be numbered {assigned}, not {row.No}. Nothing was changed.");
            }

            await this._human.SetProfileGeographyAsync(otherId, row.No, AreaListJson.Parse(row.Area), row.Latitude, row.Longitude);
        }

        foreach (var row in kept.Profiles.Where(p => otherByNo.ContainsKey(p.No)))
        {
            var existing = otherByNo[row.No];
            if (!string.Equals(row.Name ?? string.Empty, existing.Name ?? string.Empty, StringComparison.Ordinal)
                || !SameSchedule(row.ActiveHours, existing.ActiveHours))
            {
                await this._human.UpdateProfileAsync(otherId, JsonSerializer.SerializeToElement(new
                {
                    profile_no = row.No,
                    name = row.Name,
                    active_hours = string.IsNullOrWhiteSpace(row.ActiveHours) ? NoSchedule : row.ActiveHours,
                }));
            }

            if (!AreaKeys(row.Area).SequenceEqual(AreaKeys(existing.Area))
                || Round(row.Latitude) != Round(existing.Latitude) || Round(row.Longitude) != Round(existing.Longitude))
            {
                await this._human.SetProfileGeographyAsync(otherId, row.No, AreaListJson.Parse(row.Area), row.Latitude, row.Longitude);
            }
        }

        foreach (var extra in extras)
        {
            await this._human.DeleteProfileAsync(otherId, extra.No);
        }
    }

    private async Task ResolvePlacesAsync(string otherId, Snapshot kept, Snapshot other)
    {
        var otherByLabel = other.Places.ToDictionary(p => p.Label, StringComparer.Ordinal);
        var keptLabels = kept.Places.Select(p => p.Label).ToHashSet(StringComparer.Ordinal);

        foreach (var place in kept.Places)
        {
            if (!otherByLabel.TryGetValue(place.Label, out var existing))
            {
                var refusal = await this._human.AddPlaceAsync(otherId, place);
                if (refusal is not null)
                {
                    throw new PoracleRequestRefusedException(refusal);
                }
            }
            else if (Round(existing.Latitude) != Round(place.Latitude) || Round(existing.Longitude) != Round(place.Longitude))
            {
                await this._human.UpdatePlaceAsync(otherId, place.Label, place.Latitude, place.Longitude);
            }
        }

        foreach (var place in other.Places.Where(p => !keptLabels.Contains(p.Label)))
        {
            await this._human.DeletePlaceAsync(otherId, place.Label);
        }
    }

    /// <summary>Rules per profile number, across every alarm type in the all-profiles read.</summary>
    private async Task<Dictionary<int, int>> CountRulesAsync(string humanId)
    {
        var all = await this._tracking.GetAllTrackingAllProfilesAsync(humanId);
        var counts = new Dictionary<int, int>();
        if (all.ValueKind != JsonValueKind.Object)
        {
            return counts;
        }

        foreach (var type in all.EnumerateObject().Where(t => t.Value.ValueKind == JsonValueKind.Array && t.Name != "profile"))
        {
            foreach (var rule in type.Value.EnumerateArray())
            {
                if (rule.ValueKind == JsonValueKind.Object && rule.TryGetProperty("profile_no", out var no) && no.TryGetInt32(out var n))
                {
                    counts[n] = counts.GetValueOrDefault(n) + 1;
                }
            }
        }

        return counts;
    }

    private sealed record ProfileRow(int No, string? Name, string? Area, double Latitude, double Longitude, string? ActiveHours);

    private sealed record Snapshot(
        double Lat, double Lon, int Current, List<string> Areas, List<ProfileRow> Profiles, List<SavedPlace> Places);
}
