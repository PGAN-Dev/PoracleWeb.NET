using System.Globalization;
using System.Runtime.ExceptionServices;
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

    public async Task<LinkSide?> GetLegacyProfileZeroAsync(LinkedPair pair)
    {
        if (!BothExist(pair))
        {
            return null;
        }

        var discordRead = this.ReadAsync(pair.Discord!.HumanId);
        var telegramRead = this.ReadAsync(pair.Telegram!.HumanId);
        await Task.WhenAll(discordRead, telegramRead);
        var discord = discordRead.Result.HasProfileZero;
        var telegram = telegramRead.Result.HasProfileZero;
        return discord == telegram ? null : discord ? LinkSide.Discord : LinkSide.Telegram;
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
        var otherSide = keep == LinkSide.Discord ? LinkSide.Telegram : LinkSide.Discord;

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
                    // PoracleWeb.NET authorises the names for the human they are written to. The area limit is not
                    // applied: the kept side already holds this list, and the profile copies below aren't limited either.
                    await this._areas.MirrorSelectionAsync(otherId, kept.Areas);
                    break;
                case DriftField.CurrentProfile:
                    // Switching never creates a profile: one the other side lacks is the profiles line's to settle,
                    // and saying so beats PoracleNG's bare "profile not found".
                    if (!other.Profiles.Any(p => p.No == kept.Current))
                    {
                        throw new DriftProfileMissingException(otherSide, kept.Current, kept.Profiles.FirstOrDefault(p => p.No == kept.Current)?.Name);
                    }

                    await this._human.SwitchProfileAsync(otherId, kept.Current);
                    break;
                case DriftField.Profiles:
                    await this.ResolveProfilesAsync(otherId, otherSide, kept, other, progress);
                    break;
                case DriftField.Places:
                    await this.ResolvePlacesAsync(otherId, otherSide, kept, other, progress);
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

        // Profiles come before the active profile: switching to a profile the other side lacks needs the profiles
        // settled first, and the list reads in the order it can be worked through.
        // Each side's active profile is compared through the humans' location and areas, which have lines of their own.
        var live = new HashSet<int> { discord.Current, telegram.Current };
        Add(items, DriftField.Profiles, ProfilesOf(discord, live), ProfilesOf(telegram, live), ProfileDetails(discord, telegram));

        // Profile 0 is outside the pair: PoracleNG never numbers a new profile 0, so the other side could not be switched to it.
        if (discord.Current != 0 && telegram.Current != 0)
        {
            Add(items, DriftField.CurrentProfile, JsonSerializer.SerializeToElement(discord.Current), JsonSerializer.SerializeToElement(telegram.Current),
                CurrentProfileDetails(discord, telegram));
        }

        Add(items, DriftField.Places, PlacesOf(discord), PlacesOf(telegram), PlaceDetails(discord, telegram));
        return items;
    }

    /// <summary>Each side's active profile by name, what the other side calls the profile at that number, and whether it has one to switch to.</summary>
    private static JsonElement CurrentProfileDetails(Snapshot discord, Snapshot telegram)
    {
        static object Side(Snapshot self, Snapshot other) => new
        {
            name = self.Profiles.FirstOrDefault(p => p.No == self.Current)?.Name,
            otherHas = other.Profiles.Any(p => p.No == self.Current),
            otherName = other.Profiles.FirstOrDefault(p => p.No == self.Current)?.Name,
        };

        return JsonSerializer.SerializeToElement(new { discord = Side(discord, telegram), telegram = Side(telegram, discord) });
    }

    /// <summary>Each side's active profile, so the panel can say when keeping one side's profiles moves the other app.</summary>
    private static JsonElement ProfileDetails(Snapshot discord, Snapshot telegram) =>
        JsonSerializer.SerializeToElement(new { discord = new { current = discord.Current }, telegram = new { current = telegram.Current } });

    /// <summary>The labels as the person typed them, keyed by the lowercase form the comparison uses; Discord's spelling first.</summary>
    private static JsonElement PlaceDetails(Snapshot discord, Snapshot telegram)
    {
        var labels = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var place in discord.Places.Concat(telegram.Places))
        {
            labels.TryAdd(place.Label.ToLowerInvariant(), place.Label);
        }

        return JsonSerializer.SerializeToElement(new { labels });
    }

    private static void Add(List<DriftItem> items, DriftField field, JsonElement discord, JsonElement telegram, JsonElement? details = null)
    {
        var d = discord.GetRawText();
        var t = telegram.GetRawText();
        if (string.Equals(d, t, StringComparison.Ordinal))
        {
            return;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(d + "\n" + t));
        items.Add(new DriftItem(field, discord, telegram, Convert.ToHexStringLower(hash), details));
    }

    private static double Round(double value) => Math.Round(value, 5, MidpointRounding.AwayFromZero);

    private static JsonElement LocationOf(Snapshot s) => JsonSerializer.SerializeToElement(new { lat = Round(s.Lat), lon = Round(s.Lon) });

    private static string Key(string name) => UserAreaPolicy.Key(name);

    private static string[] Keys(IEnumerable<string> areas) =>
        [.. areas.Select(Key).Where(k => k.Length > 0).Distinct().Order(StringComparer.Ordinal)];

    private static JsonElement AreasOf(IEnumerable<string> areas) => JsonSerializer.SerializeToElement(Keys(areas));

    private static string[] AreaKeys(string? json) => Keys(AreaListJson.Parse(json));

    /// <summary>
    /// The profiles as compared. The areas and location of <paramref name="live"/>, the profiles either side is on, are
    /// left out: each human holds that profile's live copy, which the location and areas lines already compare, and
    /// comparing them again reported one cause on two lines. Once both sides are on the same profile, its geography is
    /// what those lines compare; a profile neither side is on is compared here.
    /// </summary>
    private static JsonElement ProfilesOf(Snapshot s, IReadOnlySet<int> live) => JsonSerializer.SerializeToElement(
        s.Profiles.OrderBy(p => p.No).Select(p => new
        {
            no = p.No,
            name = p.Name ?? string.Empty,
            active_hours = Hours(p.ActiveHours),
            areas = live.Contains(p.No) ? null : AreaKeys(p.Area),
            lat = live.Contains(p.No) ? (double?)null : Round(p.Latitude),
            lon = live.Contains(p.No) ? (double?)null : Round(p.Longitude),
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
        var hasProfileZero = false;
        if (array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                // Profile 0 is a legacy profile outside the pair: never compared, copied, renamed, switched to or deleted.
                if (item.GetIntProp("profile_no") == 0)
                {
                    hasProfileZero = true;
                    continue;
                }

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
            [.. areas], profiles, places.Named, hasProfileZero);
    }

    /// <summary>Whether a real write has landed, which decides what a failure may say to the person.</summary>
    private sealed class Progress
    {
        public bool Wrote { get; set; }
    }

    private async Task ResolveProfilesAsync(string otherId, LinkSide otherSide, Snapshot kept, Snapshot other, Progress progress)
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
                    throw new DriftProfileHoldsAlarmsException(otherSide, extra.No, extra.Name, count);
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

        // Fillers are deleted whatever happens, in the finally. Making one is a write: if deleting it fails it is
        // still there, so a later failure must not claim nothing changed.
        var fillers = new List<int>();
        var planDone = false;
        try
        {
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
                    throw new PoracleRequestRefusedException(
                        $"The other account's next profile would be numbered {assigned}, not {no}. "
                        + (progress.Wrote ? "Some changes were made before this failed. Look at the differences on Linked accounts again." : "Nothing was changed."),
                        409);
                }

                progress.Wrote = true;
                if (row is null)
                {
                    fillers.Add(no);
                    continue;
                }

                await this._human.SetProfileGeographyAsync(otherId, row.No, await this._areas.FilterAllowedAsync(otherId, AreaListJson.Parse(row.Area)), row.Latitude, row.Longitude);
            }

            planDone = true;
        }
        finally
        {
            await this.DeleteFillersAsync(otherId, fillers, rethrow: planDone);
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

            // A profile either side is on is compared through the humans' own location and areas, which have lines of
            // their own; writing its geography here would change them behind the person's back.
            var live = row.No == kept.Current || row.No == other.Current;
            if (!live
                && (!AreaKeys(row.Area).SequenceEqual(AreaKeys(existing.Area))
                    || Round(row.Latitude) != Round(existing.Latitude) || Round(row.Longitude) != Round(existing.Longitude)))
            {
                progress.Wrote = true;
                await this._human.SetProfileGeographyAsync(otherId, row.No, await this._areas.FilterAllowedAsync(otherId, AreaListJson.Parse(row.Area)), row.Latitude, row.Longitude);
            }
        }

        // Deleting the profile the other side is on would leave PoracleNG to pick where it lands.
        // The kept side may itself be on profile 0, which the other side cannot be switched to: use its lowest real one.
        if (extras.Any(e => e.No == other.Current))
        {
            var target = kept.Current != 0 ? kept.Current : kept.Profiles.Count > 0 ? kept.Profiles.Min(p => p.No) : 0;
            if (target != 0)
            {
                progress.Wrote = true;
                await this._human.SwitchProfileAsync(otherId, target);
            }
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

    /// <summary>
    /// Deletes every filler, carrying on past a failure. After a failure elsewhere the original error is the answer,
    /// so a failed delete is dropped; on the success path the first one is thrown once all have been tried.
    /// </summary>
    private async Task DeleteFillersAsync(string humanId, List<int> fillers, bool rethrow)
    {
        Exception? first = null;
        foreach (var made in fillers)
        {
            try
            {
                await this._human.DeleteProfileAsync(humanId, made);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                first ??= ex;
            }
        }

        if (rethrow && first is not null)
        {
            ExceptionDispatchInfo.Throw(first);
        }
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

    private async Task ResolvePlacesAsync(string otherId, LinkSide otherSide, Snapshot kept, Snapshot other, Progress progress)
    {
        // PoracleNG's label is unique per human and the stored labels are compared the way AlarmMover
        // compares them, ignoring case; an update or delete uses the other side's own spelling.
        var otherByLabel = other.Places.GroupBy(p => p.Label, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var keptLabels = kept.Places.Select(p => p.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var extras = other.Places.Where(p => !keptLabels.Contains(p.Label)).ToList();

        // PoracleNG refuses to delete a place alerts still point at; find out before anything is written. Every
        // profile's rules count: a place is the human's, not one profile's, and the active profile alone missed
        // rules on the others until the delete refused them halfway through.
        if (extras.Count > 0)
        {
            var rules = await this._tracking.GetStoredRulesAsync(otherId);
            foreach (var extra in extras)
            {
                var inUse = rules.Count(r => PlaceUsage.HasPlace(r.Rule)
                    && string.Equals(r.Rule.GetProperty("override_location_label").GetString(), extra.Label, StringComparison.Ordinal));
                if (inUse > 0)
                {
                    throw new DriftPlaceInUseException(otherSide, extra.Label, inUse);
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
                throw new DriftPlaceInUseException(otherSide, place.Label, ex.ReferencingRules.Count);
            }
        }
    }

    private sealed record ProfileRow(int No, string? Name, string? Area, double Latitude, double Longitude, string? ActiveHours);

    private sealed record Snapshot(
        double Lat, double Lon, int Current, List<string> Areas, List<ProfileRow> Profiles, List<SavedPlace> Places, bool HasProfileZero);
}
