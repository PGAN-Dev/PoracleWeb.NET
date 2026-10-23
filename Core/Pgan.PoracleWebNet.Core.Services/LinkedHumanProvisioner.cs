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
    ILogger<LinkedHumanProvisioner> logger) : ILinkedHumanProvisioner
{
    private const string Refused = "Nothing was linked.";

    private readonly IPoracleHumanProxy _humanProxy = humanProxy;
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
            ["area"] = AreaListJson.Parse(source.Area),
            ["community"] = AreaListJson.Parse(source.CommunityMembership),
        };
        if (!string.IsNullOrWhiteSpace(source.Language))
        {
            body["language"] = source.Language;
        }

        var active = sourceProfiles.Find(p => p.No == source.CurrentProfileNo);
        if (!string.IsNullOrWhiteSpace(active?.Name))
        {
            body["profile_name"] = active.Name;
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
            await this.CopyProfilesAndPlacesAsync(sourceHumanId, newId, active, sourceProfiles);
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

    private async Task CopyProfilesAndPlacesAsync(string sourceId, string newId, ProfileRow? active, List<ProfileRow> sourceProfiles)
    {
        // The source's active profile is what the human-level fields describe, so it maps onto the new
        // human's own default profile; the rest are created beside it.
        var created = await this.ReadProfilesAsync(newId);
        var defaultNo = created.Count > 0 ? created.Min(p => p.No) : 1;
        if (active is not null && !string.IsNullOrWhiteSpace(active.ActiveHours) && active.ActiveHours != "[]")
        {
            await this._humanProxy.UpdateProfileAsync(newId, JsonSerializer.SerializeToElement(new
            {
                profile_no = defaultNo,
                name = active.Name,
                active_hours = active.ActiveHours,
            }));
        }

        foreach (var profile in sourceProfiles.Where(p => active is null || p.No != active.No).OrderBy(p => p.No))
        {
            var no = await this._humanProxy.AddProfileAsync(newId, JsonSerializer.SerializeToElement(new
            {
                name = profile.Name,
                active_hours = profile.ActiveHours,
            }));
            await this._humanProxy.SetProfileGeographyAsync(newId, no, AreaListJson.Parse(profile.Area), profile.Latitude, profile.Longitude);
        }

        var places = await this._humanProxy.GetPlacesAsync(sourceId);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Saved place {Label} was not copied to the linked account: {Reason}")]
    private static partial void LogPlaceSkipped(ILogger logger, string label, string reason);
}
