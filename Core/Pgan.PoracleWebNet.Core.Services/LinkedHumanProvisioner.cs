using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Models.Helpers;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Creates the missing side of a link as a Poracle human copied from the caller's: identity-level fields,
/// profiles with their schedules, areas and locations, and saved places. Alerts are never copied.
/// </summary>
public partial class LinkedHumanProvisioner(
    IPoracleHumanProxy humanProxy,
    IHumanService humans,
    IUserAreaWriter areaWriter,
    ILogger<LinkedHumanProvisioner> logger) : ILinkedHumanProvisioner
{
    private const string Refused = "Nothing was linked.";
    private const string PlaceholderName = "placeholder";

    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
    private readonly IUserAreaWriter _areaWriter = areaWriter;
    private readonly IHumanService _humans = humans;
    private readonly ILogger<LinkedHumanProvisioner> _logger = logger;

    public async Task CreateMirrorAsync(string sourceHumanId, LinkSide newSide, string newId, string newName)
    {
        var source = await this._humans.GetByIdAsync(sourceHumanId)
            ?? throw new LinkRefusalException(LinkRefusalCode.ProvisionFailed, Refused);
        var type = TypeOf(newSide);
        var sourceProfiles = await this.ReadProfilesAsync(sourceHumanId);

        var body = new Dictionary<string, object?>
        {
            ["id"] = newId,
            ["name"] = newName,
            ["type"] = type,
            ["latitude"] = source.Latitude,
            ["longitude"] = source.Longitude,
            ["community"] = AreaListJson.Parse(source.CommunityMembership),
        };
        if (!string.IsNullOrWhiteSpace(source.Language))
        {
            body["language"] = source.Language;
        }

        // Profile numbers must agree across the pair, so the create's default profile (number 1) is
        // named for the source's profile 1; areas are written afterwards through the trusted per-profile
        // write, because an untrusted filter on create can drop a drawn fence.
        var first = sourceProfiles.Find(p => p.No == 1);
        if (!string.IsNullOrWhiteSpace(first?.Name))
        {
            body["profile_name"] = first.Name;
        }

        try
        {
            await this._humanProxy.CreateHumanAsync(JsonSerializer.SerializeToElement(body));
        }
        catch (Exception ex)
        {
            // A create can commit and still fail; whatever is there is removed only if it is ours.
            await this.TryRollBackAsync(newId, type, newName);
            throw new LinkRefusalException(LinkRefusalCode.ProvisionFailed, Refused, ex);
        }

        try
        {
            await this.CopyProfilesAndPlacesAsync(source, newId, sourceProfiles);
        }
        catch (Exception ex)
        {
            await this.TryRollBackAsync(newId, type, newName);
            throw new LinkRefusalException(LinkRefusalCode.ProvisionFailed, Refused, ex);
        }
    }

    public Task RollBackAsync(string newId, LinkSide newSide, string newName) =>
        this.RollBackCoreAsync(newId, TypeOf(newSide), newName);

    private static string TypeOf(LinkSide side) =>
        side == LinkSide.Discord ? AccountLinkKeys.DiscordUserType : AccountLinkKeys.TelegramUserType;

    /// <summary>
    /// Makes mirror profile N a copy of source profile N for every N. PoracleNG gives a new profile the
    /// lowest free number and says which, so the numbers are walked upward; a gap in the source is filled
    /// with a placeholder that is deleted once the real profiles exist. Any number other than the expected
    /// one is a failure, since later moves and drift checks compare profile numbers.
    /// </summary>
    /// <summary>
    /// The copied areas the person may hold, the check profile create applies: the geography write is trusted, so
    /// without it a name Poracle would refuse (another user's fence, a community the new account is outside)
    /// would land on the new account. The new human is not linked yet, so its own allowed set cannot see the
    /// person's drawn fences, which belong to the source id; a name either of the two accounts may select is kept.
    /// </summary>
    private async Task<List<string>> AllowedAreasAsync(string sourceId, string newId, List<string> names)
    {
        var requested = names.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        if (requested.Count == 0)
        {
            return [];
        }

        var keep = (await this._areaWriter.FilterAllowedAsync(newId, requested))
            .Concat(await this._areaWriter.FilterAllowedAsync(sourceId, requested))
            .Select(UserAreaPolicy.Key)
            .ToHashSet(StringComparer.Ordinal);
        var kept = requested.Where(n => keep.Contains(UserAreaPolicy.Key(n))).ToList();
        if (kept.Count < requested.Count)
        {
            LogAreasDropped(this._logger, string.Join(", ", requested.Except(kept)));
        }

        return kept;
    }

    private async Task CopyProfilesAndPlacesAsync(Human source, string newId, List<ProfileRow> sourceProfiles)
    {
        var created = await this.ReadProfilesAsync(newId);
        var defaultNo = created.Count > 0 ? created.Min(p => p.No) : 1;
        if (defaultNo != 1)
        {
            throw new InvalidOperationException($"The new human's default profile is {defaultNo}, not 1.");
        }

        var placeholders = new List<int>();
        var byNo = sourceProfiles.GroupBy(p => p.No).ToDictionary(g => g.Key, g => g.First());
        var highest = byNo.Count > 0 ? byNo.Keys.Max() : 1;

        if (byNo.Count == 0)
        {
            // No profile rows to copy: the human's own fields describe its one profile.
            await this._humanProxy.SetProfileGeographyAsync(newId, 1, await this.AllowedAreasAsync(source.Id, newId, AreaListJson.Parse(source.Area)), source.Latitude, source.Longitude);
        }

        for (var n = 1; n <= highest; n++)
        {
            byNo.TryGetValue(n, out var row);
            if (n == 1)
            {
                if (row is null)
                {
                    placeholders.Add(1);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(row.ActiveHours) && row.ActiveHours != "[]")
                {
                    await this._humanProxy.UpdateProfileAsync(newId, JsonSerializer.SerializeToElement(new
                    {
                        profile_no = 1,
                        name = row.Name,
                        active_hours = row.ActiveHours,
                    }));
                }
            }
            else
            {
                var assigned = await this._humanProxy.AddProfileAsync(newId, row is null
                    ? JsonSerializer.SerializeToElement(new { name = PlaceholderName })
                    : JsonSerializer.SerializeToElement(new { name = row.Name, active_hours = row.ActiveHours }));
                if (assigned != n)
                {
                    throw new InvalidOperationException($"PoracleNG numbered a copied profile {assigned}, expected {n}.");
                }

                if (row is null)
                {
                    placeholders.Add(n);
                    continue;
                }
            }

            await this._humanProxy.SetProfileGeographyAsync(newId, n, await this.AllowedAreasAsync(source.Id, newId, AreaListJson.Parse(row!.Area)), row.Latitude, row.Longitude);
        }

        if (byNo.Count > 0)
        {
            var target = byNo.ContainsKey(source.CurrentProfileNo) ? source.CurrentProfileNo : byNo.Keys.Min();
            if (target != 1)
            {
                await this._humanProxy.SwitchProfileAsync(newId, target);
            }
        }

        foreach (var number in placeholders)
        {
            await this._humanProxy.DeleteProfileAsync(newId, number);
        }

        var places = await this._humanProxy.GetPlacesAsync(source.Id);
        foreach (var place in places.Named)
        {
            var refusal = await this._humanProxy.AddPlaceAsync(newId, place);
            if (refusal is not null)
            {
                LogPlaceSkipped(this._logger, place.Label, refusal);
            }
        }
    }

    private async Task<List<ProfileRow>> ReadProfilesAsync(string humanId)
    {
        var json = await this._humanProxy.GetProfilesAsync(humanId);
        var array = json.ValueKind == JsonValueKind.Array ? json
            : json.ValueKind == JsonValueKind.Object && json.TryGetProperty("profile", out var p) ? p
            : default;
        var rows = new List<ProfileRow>();
        if (array.ValueKind != JsonValueKind.Array)
        {
            return rows;
        }

        foreach (var item in array.EnumerateArray())
        {
            // A legacy profile 0 stays on the source: PoracleNG never numbers a new profile 0, so it cannot be mirrored.
            if (item.GetIntProp("profile_no") == 0)
            {
                continue;
            }

            rows.Add(new ProfileRow(
                item.GetIntProp("profile_no"),
                item.GetStringPropOrNull("name"),
                item.GetStringPropOrNull("area"),
                item.GetDoubleProp("latitude"),
                item.GetDoubleProp("longitude"),
                item.GetStringPropOrNull("active_hours")));
        }

        return rows;
    }

    private async Task TryRollBackAsync(string newId, string type, string newName)
    {
        try
        {
            await this.RollBackCoreAsync(newId, type, newName);
        }
        catch (Exception ex)
        {
            LogRollbackFailed(this._logger, ex);
        }
    }

    // The #1028 rule: another request may have created this id, and that row is not ours to delete.
    private async Task RollBackCoreAsync(string newId, string type, string newName)
    {
        var found = await this._humanProxy.GetHumanAsync(newId);
        if (found is not { } human)
        {
            return;
        }

        if (string.Equals(human.GetStringPropOrNull("type"), type, StringComparison.Ordinal)
            && string.Equals(human.GetStringPropOrNull("name"), newName, StringComparison.Ordinal))
        {
            await this._humanProxy.DeleteHumanAsync(newId);
        }
        else
        {
            LogRollbackSkipped(this._logger);
        }
    }

    private sealed record ProfileRow(int No, string? Name, string? Area, double Latitude, double Longitude, string? ActiveHours);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not remove a half-created linked human; it may be left behind")]
    private static partial void LogRollbackFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Left a human alone during link rollback: its type or name is not what this link created")]
    private static partial void LogRollbackSkipped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Areas {Names} were not copied to the linked account: not selectable by it or by the person's own account")]
    private static partial void LogAreasDropped(ILogger logger, string names);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Saved place {Label} was not copied to the linked account: {Reason}")]
    private static partial void LogPlaceSkipped(ILogger logger, string label, string reason);
}
