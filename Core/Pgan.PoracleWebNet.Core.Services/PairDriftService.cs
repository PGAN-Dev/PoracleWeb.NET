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
/// Resolve writes the kept value to the other human only: the paired writer would write both and so re-write
/// the side being kept. Area names go through <see cref="IUserAreaWriter"/> so they are authorised for the
/// human they are written to.
/// </summary>
public sealed class PairDriftService(IPoracleHumanProxy human, IPoracleTrackingProxy tracking, IUserAreaWriter areas) : IPairDriftService
{
    private const string FillerName = "placeholder";
    private const string NoSchedule = "[]";

    private readonly IPoracleHumanProxy _human = human;
    private readonly IPoracleTrackingProxy _tracking = tracking;
    private readonly IUserAreaWriter _areas = areas;

    public async Task<IReadOnlyList<DriftItem>> GetAsync(LinkedPair pair)
    {
        if (!BothExist(pair))
        {
            return [];
        }

        var discordRead = this.ReadAsync(pair.Discord!.HumanId);
        var telegramRead = this.ReadAsync(pair.Telegram!.HumanId);
        await Task.WhenAll(discordRead, telegramRead);
        var discord = discordRead.Result;
        var telegram = telegramRead.Result;
        return Compare(discord, telegram);
    }

    public async Task ResolveAsync(LinkedPair pair, DriftField field, LinkSide keep, string expected)
    {
        if (!BothExist(pair))
        {
            throw new DriftChangedException();
        }

        var discordRead = this.ReadAsync(pair.Discord!.HumanId);
        var telegramRead = this.ReadAsync(pair.Telegram!.HumanId);
        await Task.WhenAll(discordRead, telegramRead);
        var discord = discordRead.Result;
        var telegram = telegramRead.Result;

        // Judged on a fresh read: the banner may be minutes old and a bot command may have moved things since.
        var item = Compare(discord, telegram).FirstOrDefault(i => i.Field == field);
        if (item is null || !string.Equals(item.Fingerprint, expected, StringComparison.Ordinal))
        {
            throw new DriftChangedException();
        }

        var kept = keep == LinkSide.Discord ? discord : telegram;
        var other = keep == LinkSide.Discord ? telegram : discord;
        var otherId = keep == LinkSide.Discord ? pair.Telegram!.HumanId : pair.Discord!.HumanId;

        var progress = new Progress();
        try
        {
            switch (field)
            {
                case DriftField.Location:
                    await this._human.SetLocationAsync(otherId, kept.Lat, kept.Lon);
                    break;
                case DriftField.Areas:
                    // Through the writer, not the raw trusted route: a trusted write skips PoracleNG's own area check, so
                    // PoracleWeb.NET authorises the names for the human they are written to.
                    await this._areas.SaveSelectionAsync(otherId, kept.Areas);
                    break;
                case DriftField.CurrentProfile:
                    await this._human.SwitchProfileAsync(otherId, kept.Current);
                    break;
                case DriftField.Profiles:
                    await this.ResolveProfilesAsync(otherId, kept, other, progress);
                    break;
                case DriftField.Places:
                    await this.ResolvePlacesAsync(otherId, kept, other, progress);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(field));
            }
        }
        catch (Exception ex) when (progress.Wrote && ex is not OperationCanceledException)
        {
            throw new DriftPartiallyAppliedException(ex);
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
        s.Places.OrderBy(p => p.Label.ToLowerInvariant(), StringComparer.Ordinal)
            .Select(p => new { label = p.Label.ToLowerInvariant(), lat = Round(p.Latitude), lon = Round(p.Longitude) }).ToArray());

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

    /// <summary>Whether a real write has landed, which decides what a failure may say to the person.</summary>
    private sealed class Progress
    {
        public bool Wrote { get; set; }
    }

    private async Task ResolveProfilesAsync(string otherId, Snapshot kept, Snapshot other, Progress progress)
    {
        var otherByNo = other.Profiles.ToDictionary(p => p.No);
        var keptNumbers = kept.Profiles.Select(p => p.No).ToHashSet();
        var extras = other.Profiles.Where(p => !keptNumbers.Contains(p.No)).OrderBy(p => p.No).ToList();

        // Everything refusable is checked before the first write: a profile that holds alerts is never
        // deleted for the sake of a match, and a refusal here changes nothing.
        if (extras.Count > 0)
        {
            var counts = (await this._tracking.GetStoredRulesAsync(otherId))
                .GroupBy(r => r.ProfileNo).ToDictionary(g => g.Key, g => g.Count());
            foreach (var extra in extras)
            {
                if (counts.TryGetValue(extra.No, out var count) && count > 0)
                {
                    throw new DriftProfileHoldsAlarmsException(extra.No, extra.Name, count);
                }
            }
        }

        // PoracleNG gives a new profile the lowest free number, so a gap in the kept numbering is
        // reached by creating filler profiles up to it and deleting them afterwards.
        var plan = new List<(int No, ProfileRow? Row)>();
        var occupied = otherByNo.Keys.ToHashSet();
        foreach (var row in kept.Profiles.Where(p => !otherByNo.ContainsKey(p.No)).OrderBy(p => p.No))
        {
            while (LowestFree(occupied) < row.No)
            {
                var filler = LowestFree(occupied);
                occupied.Add(filler);
                plan.Add((filler, null));
            }

            occupied.Add(row.No);
            plan.Add((row.No, row));
        }

        var fillers = new List<int>();
        foreach (var (no, row) in plan)
        {
            var assigned = await this._human.AddProfileAsync(otherId, JsonSerializer.SerializeToElement(new
            {
                name = row?.Name ?? FillerName,
                active_hours = string.IsNullOrWhiteSpace(row?.ActiveHours) ? NoSchedule : row!.ActiveHours,
            }));
            if (assigned != no)
            {
                await this.TryDeleteAsync(otherId, assigned);
                foreach (var made in fillers)
                {
                    await this.TryDeleteAsync(otherId, made);
                }

                throw new PoracleRequestRefusedException(
                    $"The other account's next profile would be numbered {assigned}, not {no}. "
                    + (progress.Wrote ? "Some changes were made before this failed. Check the banner." : "Nothing was changed."),
                    409);
            }

            if (row is null)
            {
                fillers.Add(no);
                continue;
            }

            progress.Wrote = true;
            await this._human.SetProfileGeographyAsync(otherId, row.No, await this._areas.FilterAllowedAsync(otherId, AreaListJson.Parse(row.Area)), row.Latitude, row.Longitude);
        }

        foreach (var made in fillers)
        {
            await this._human.DeleteProfileAsync(otherId, made);
        }

        foreach (var row in kept.Profiles.Where(p => otherByNo.ContainsKey(p.No)))
        {
            var existing = otherByNo[row.No];
            if (!string.Equals(row.Name ?? string.Empty, existing.Name ?? string.Empty, StringComparison.Ordinal)
                || !SameSchedule(row.ActiveHours, existing.ActiveHours))
            {
                progress.Wrote = true;
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
                progress.Wrote = true;
                await this._human.SetProfileGeographyAsync(otherId, row.No, await this._areas.FilterAllowedAsync(otherId, AreaListJson.Parse(row.Area)), row.Latitude, row.Longitude);
            }
        }

        // Deleting the profile the other side is on would leave PoracleNG to pick where it lands.
        if (extras.Any(e => e.No == other.Current))
        {
            progress.Wrote = true;
            await this._human.SwitchProfileAsync(otherId, kept.Current);
        }

        foreach (var extra in extras)
        {
            progress.Wrote = true;
            await this._human.DeleteProfileAsync(otherId, extra.No);
        }
    }

    private static int LowestFree(HashSet<int> occupied)
    {
        var n = 1;
        while (occupied.Contains(n))
        {
            n++;
        }

        return n;
    }

    private async Task TryDeleteAsync(string humanId, int profileNo)
    {
        try
        {
            await this._human.DeleteProfileAsync(humanId, profileNo);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best effort: the refusal being raised is the answer the person needs.
        }
    }

    private async Task ResolvePlacesAsync(string otherId, Snapshot kept, Snapshot other, Progress progress)
    {
        // PoracleNG's label is unique per human and the stored labels are compared the way AlarmMover
        // compares them, ignoring case; an update or delete uses the other side's own spelling.
        var otherByLabel = other.Places.GroupBy(p => p.Label, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var keptLabels = kept.Places.Select(p => p.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var extras = other.Places.Where(p => !keptLabels.Contains(p.Label)).ToList();

        // PoracleNG refuses to delete a place alerts still point at; find out before anything is written.
        if (extras.Count > 0)
        {
            var snapshot = await this._tracking.GetAllTrackingWithDescriptionsAsync(otherId);
            foreach (var extra in extras)
            {
                var inUse = PlaceUsage.RulesAimedAt(snapshot, extra.Label).Count;
                if (inUse > 0)
                {
                    throw new DriftPlaceInUseException(extra.Label, inUse);
                }
            }
        }

        foreach (var place in kept.Places)
        {
            if (!otherByLabel.TryGetValue(place.Label, out var existing))
            {
                var refusal = await this._human.AddPlaceAsync(otherId, place);
                if (refusal is not null)
                {
                    throw new PoracleRequestRefusedException(refusal);
                }

                progress.Wrote = true;
            }
            else if (Round(existing.Latitude) != Round(place.Latitude) || Round(existing.Longitude) != Round(place.Longitude))
            {
                progress.Wrote = true;
                await this._human.UpdatePlaceAsync(otherId, existing.Label, place.Latitude, place.Longitude);
            }
        }

        foreach (var place in extras)
        {
            try
            {
                await this._human.DeletePlaceAsync(otherId, place.Label);
                progress.Wrote = true;
            }
            catch (PlaceInUseException ex)
            {
                throw new DriftPlaceInUseException(place.Label, ex.ReferencingRules.Count);
            }
        }
    }

    private sealed record ProfileRow(int No, string? Name, string? Area, double Latitude, double Longitude, string? ActiveHours);

    private sealed record Snapshot(
        double Lat, double Lon, int Current, List<string> Areas, List<ProfileRow> Profiles, List<SavedPlace> Places);
}
