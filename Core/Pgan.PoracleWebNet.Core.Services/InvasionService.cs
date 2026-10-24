using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public partial class InvasionService(IPoracleTrackingProxy proxy, IFeatureGate featureGate, ILogger<InvasionService> logger, ITrackedUidRemapper uidRemapper, IPoracleIncidentProxy incidentProxy) : IInvasionService
{
    private const string TrackingType = "invasion";
    private readonly IPoracleTrackingProxy _proxy = proxy;
    private readonly IFeatureGate _featureGate = featureGate;
    private readonly ILogger<InvasionService> _logger = logger;
    private readonly ITrackedUidRemapper _uidRemapper = uidRemapper;
    private readonly IPoracleIncidentProxy _incidentProxy = incidentProxy;

    /// <summary>
    /// Whether pokestop-event rows belong to the Pokestop Events page rather than this list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Invasion and incident rows share the <c>invasion</c> table. v2's invasion routes leave the
    /// event rules out, but the proxy reads the incident list too and folds those rules back into the
    /// invasion list in v1's form (as v1 itself returned them), so this list has to do the partition
    /// PoracleNG does for the v2 endpoints.
    /// </para>
    /// <para>
    /// <strong>Conditional, and that is the whole point.</strong> When the Pokestop Events surface is
    /// unavailable — an operator who set <c>disable_showcase</c>, or a PoracleNG that could not be asked — the page that
    /// would hold these rows does not exist, and filtering them out here would leave alarms that fire
    /// and cannot be seen or deleted. Event rows are creatable from the invasion add dialog wherever the
    /// page is off, so this is not hypothetical.
    /// </para>
    /// </remarks>
    private Task<bool> EventsLiveElsewhereAsync() =>
        this._featureGate.IsEnabledAsync(DisableFeatureKeys.PokestopEvents);

    /// <summary>
    /// The same partition as <see cref="ReadOwnRowsAsync"/>, over a raw stored row, for the bulk
    /// paths that rewrite rows in place instead of round-tripping the model.
    /// </summary>
    private async Task<Func<JsonElement, bool>> OwnRowPredicateAsync()
    {
        if (!await this.EventsLiveElsewhereAsync())
        {
            return _ => true;
        }

        return row => !PokestopEventTypes.IsEventName(
            row.TryGetProperty("grunt_type", out var gt) && gt.ValueKind == JsonValueKind.String
                ? gt.GetString()
                : null);
    }

    private async Task<List<Invasion>> ReadOwnRowsAsync(string userId)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var items = DeserializeItems(json);

        return await this.EventsLiveElsewhereAsync()
            ? [.. items.Where(x => !PokestopEventTypes.IsEventName(x.GruntType))]
            : items;
    }

    /// <inheritdoc />
    public async Task<bool> BelongsToPokestopEventsAsync(string? gruntType) =>
        PokestopEventTypes.IsEventName(gruntType) && await this.EventsLiveElsewhereAsync();

    public async Task<IEnumerable<Invasion>> GetByUserAsync(string userId, int profileNo) =>
        await this.ReadOwnRowsAsync(userId);

    public async Task<Invasion?> GetByUidAsync(string userId, int uid)
    {
        var items = await this.ReadOwnRowsAsync(userId);
        return items.FirstOrDefault(x => x.Uid == uid);
    }

    public async Task<Invasion> CreateAsync(string userId, Invasion model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Invasions);
        model.Id = userId;
        RequireGruntType(model);

        if (AsPokestopEvent(model) is { } pokestopEvent)
        {
            var uids = await IncidentCreateProtocol.CreateAsync(this._incidentProxy, userId, [pokestopEvent], single: true);
            model.Uid = uids[0];
            return model;
        }

        var body = SerializeToElement(model);

        var result = await this._proxy.CreateAsync(TrackingType, userId, body);

        if (result.NewUids.Count > 0)
        {
            model.Uid = (int)result.NewUids[0];
        }

        return model;
    }

    public async Task<Invasion> UpdateAsync(string userId, Invasion model)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Invasions);
        RequireGruntType(model);
        var oldUid = model.Uid;

        // Invasion and incident rules have separate uid sequences, so the uid names a rule only within its own
        // kind. A kind change is a create of the new rule and then removal of the old one, never a write
        // addressed by the old uid, which would replace an unrelated rule of the other kind. See #1062.
        if (await this.IsKindChangeAsync(userId, oldUid, model))
        {
            model.Uid = 0;
            var created = await this.CreateAsync(userId, model);
            if (AsPokestopEvent(model) is not null)
            {
                await this._proxy.DeleteByUidAsync(TrackingType, userId, oldUid);
            }
            else
            {
                await this._incidentProxy.DeleteByUidAsync(userId, oldUid);
            }

            model.Uid = created.Uid;
            if (model.Uid > 0)
            {
                await this._uidRemapper.RemapAsync(userId, TrackingType, oldUid, model.Uid);
            }

            return model;
        }

        if (AsPokestopEvent(model) is { } pokestopEvent)
        {
            var replaced = await this._incidentProxy.ReplaceAsync(userId, oldUid, pokestopEvent);
            model.Uid = replaced.PrimaryUid ?? oldUid;
            if (model.Uid != oldUid)
            {
                // A quick pick tracks the rule under the invasion type it applied it as.
                await this._uidRemapper.RemapAsync(userId, TrackingType, oldUid, model.Uid);
            }

            return model;
        }

        var body = SerializeToElement(model);

        // Carry forward anything the stored row holds that the model does not declare. See #730.
        body = await TrackingFieldPreserver.PreserveStoredFieldsAsync(
            this._proxy, TrackingType, userId, oldUid, body);

        model.Uid = await TrackingV2Replacement.ApplyAsync(
            this._proxy, TrackingType, userId, oldUid, body, this._uidRemapper);

        return model;
    }

    /// <summary>
    /// Whether this edit moves the rule between the invasion and incident routes, judged from the stored rules
    /// that carry the uid. Reads nothing when the uid is not held, so an unknown uid takes the old path.
    /// </summary>
    private async Task<bool> IsKindChangeAsync(string userId, int uid, Invasion model)
    {
        if (uid <= 0)
        {
            return false;
        }

        JsonElement rows;
        try
        {
            rows = await this._proxy.GetByUserAsync(TrackingType, userId);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }

        if (rows.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        bool hasPlain = false, hasEvent = false;
        foreach (var row in rows.EnumerateArray())
        {
            if (PoracleJsonHelper.UidOf(row) != uid)
            {
                continue;
            }

            var isEvent = row.TryGetProperty("grunt_type", out var grunt)
                && grunt.ValueKind == JsonValueKind.String
                && PokestopEventTypes.IsEventName(grunt.GetString());
            hasEvent |= isEvent;
            hasPlain |= !isEvent;
        }

        if (AsPokestopEvent(model) is not null)
        {
            // An invasion edited into an event. When both kinds hold the number and this page also lists events,
            // the page cannot say which one was meant, so the edit stays in place as before.
            return hasPlain && (!hasEvent || await this.EventsLiveElsewhereAsync());
        }

        return hasEvent && !hasPlain;
    }

    public async Task<bool> DeleteAsync(string userId, int uid)
    {
        await this._proxy.DeleteByUidAsync(TrackingType, userId, uid);
        return true;
    }

    public async Task<int> DeleteAllByUserAsync(string userId, int profileNo)
    {
        var items = await this.ReadOwnRowsAsync(userId);
        var uids = items.Select(x => x.Uid).ToList();

        if (uids.Count == 0)
        {
            return 0;
        }

        await this._proxy.BulkDeleteByUidsAsync(TrackingType, userId, uids);
        return uids.Count;
    }

    public async Task<DistanceUpdateResult> UpdateDistanceByUserAsync(string userId, int profileNo, int distance)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        return await this.PatchDistanceAsync(userId, json, await this.OwnRowPredicateAsync(), distance);
    }

    public async Task<DistanceUpdateResult> UpdateDistanceByUidsAsync(List<int> uids, string userId, int distance)
    {
        var json = await this._proxy.GetByUserAsync(TrackingType, userId);
        var selected = new HashSet<int>(uids);
        var isOurs = await this.OwnRowPredicateAsync();

        return await this.PatchDistanceAsync(
            userId,
            json,
            row => isOurs(row) && PoracleJsonHelper.UidOf(row) is int rowUid && selected.Contains(rowUid),
            distance);
    }

    /// <summary>
    /// One v2 PATCH per route: the invasion rules to <c>invasion</c>, and any Pokestop event rules this list
    /// holds (only where there is no Pokestop Events page, see <see cref="OwnRowPredicateAsync"/>) to
    /// <c>incident</c>, since v2's invasion route does not hold them.
    /// </summary>
    private async Task<DistanceUpdateResult> PatchDistanceAsync(
        string userId, JsonElement json, Func<JsonElement, bool> include, int distance)
    {
        var plan = DistanceRewrite.Plan(DistanceRewrite.ScopesOf(json, include), distance);
        var eventUids = DistanceRewrite.ScopesOf(json, row => include(row) && IsEventRow(row))
            .Select(r => r.Uid)
            .ToHashSet();
        var invasionUids = plan.WritableUids.Where(uid => !eventUids.Contains(uid)).ToList();
        var incidentUids = plan.WritableUids.Where(eventUids.Contains).ToList();
        var set = new TrackingPatchSet(Distance: distance);
        var results = new List<TrackingPatchResult>();

        if (invasionUids.Count > 0)
        {
            results.Add(await this._proxy.PatchAsync(TrackingType, userId, invasionUids, set));
        }

        if (incidentUids.Count > 0)
        {
            results.Add(await this._incidentProxy.PatchAsync(userId, incidentUids, set));
        }

        return DistanceRewrite.Report(plan, TrackingType, results, this._logger);
    }

    private static bool IsEventRow(JsonElement row) => PokestopEventTypes.IsEventName(
        row.TryGetProperty("grunt_type", out var gt) && gt.ValueKind == JsonValueKind.String ? gt.GetString() : null);

    public async Task<int> CountByUserAsync(string userId, int profileNo)
    {
        var items = await this.ReadOwnRowsAsync(userId);
        return items.Count;
    }

    public async Task<IEnumerable<Invasion>> BulkCreateAsync(string userId, IEnumerable<Invasion> models)
    {
        await this._featureGate.EnsureEnabledAsync(DisableFeatureKeys.Invasions);
        var modelList = models.ToList();

        foreach (var model in modelList)
        {
            model.Id = userId;
            RequireGruntType(model);
        }

        // A quick pick can name a Pokestop event (an admin pick, or one saved before the events page), and
        // v2's invasion route refuses those, so they are written to the incident route in their own batch.
        var events = modelList
            .Select(model => (model, rule: AsPokestopEvent(model)))
            .Where(x => x.rule is not null)
            .ToList();
        var grunts = modelList.Where(m => !PokestopEventTypes.IsEventName(m.GruntType)).ToList();

        if (grunts.Count > 0)
        {
            var result = await this._proxy.CreateAsync(TrackingType, userId, SerializeToElement(grunts));
            for (var i = 0; i < grunts.Count && i < result.RowUids.Count; i++)
            {
                grunts[i].Uid = result.RowUids[i];
            }
        }

        if (events.Count > 0)
        {
            var uids = await IncidentCreateProtocol.CreateAsync(
                this._incidentProxy, userId, [.. events.Select(x => x.rule!)], single: false);
            for (var i = 0; i < events.Count; i++)
            {
                events[i].model.Uid = uids[i];
            }
        }

        return modelList;
    }

    /// <summary>
    /// The Pokestop-event rule an invasion model names, or null when it names a Team Rocket grunt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// kecleon, gold-stop and showcase share the invasion table, but v2's invasion route refuses them ("use
    /// /incident"), so they are written as <c>incident</c> rules: the name becomes the event's
    /// <c>display_type</c>, and distance, template, clean, mention and override scope are carried across.
    /// </para>
    /// <para>
    /// An event has no gender. A gender other than any is refused rather than dropped, since the model would
    /// otherwise be saved as something other than what was sent. No stored event rule carries one.
    /// </para>
    /// </remarks>
    internal static PokestopEvent? AsPokestopEvent(Invasion model)
    {
        if (PokestopEventTypes.DisplayTypeFor(model.GruntType) is not int displayType)
        {
            return null;
        }

        if (model.Gender != 0)
        {
            throw new AlarmValidationException(
                $"PoracleNG can't store this alarm as it is: a Pokestop event ({model.GruntType}) has no gender. Nothing was changed.");
        }

        return new PokestopEvent
        {
            Uid = model.Uid,
            Id = model.Id,
            DisplayType = displayType,
            Distance = model.Distance,
            Template = model.Template,
            Clean = model.Clean,
            Ping = model.Ping,
            OverrideLocationLabel = model.OverrideLocationLabel,
            OverrideAreas = model.OverrideAreas,
        };
    }

    /// <summary>
    /// PoracleNG rejects an empty <c>grunt_type</c> with <c>400 "Grunt type mandatory"</c> and has no
    /// catch-all keyword, so coalescing a missing value to <c>""</c> guaranteed a failure that surfaced
    /// as a generic 500. Fail here instead, where the message says what is actually wrong. Callers that
    /// want "everything" must fan out over <see cref="InvasionGruntTypes.All"/>. See #416.
    /// </summary>
    /// <summary>
    /// The grunt_type column width upstream: <c>varchar(255)</c>, per PoracleNG's initial schema
    /// migration at the commit production runs. This said 35, which was an invented limit wearing a
    /// factual justification -- in a fix whose whole point was refusing the impossible rather than
    /// allowing only the known. See #661.
    /// </summary>
    private const int MaxGruntTypeLength = 255;

    private static void RequireGruntType(Invasion model)
    {
        model.GruntType = InvasionGruntTypes.Normalise(model.GruntType);

        // Deliberately NOT an allowlist. The live database holds grunt types this codebase does not model --
        // blanche, candela, spark, "npc 0" through "npc 10", "player team leader" -- so validating against
        // InvasionGruntTypes.All would have refused edits to alarms that work today. What is checked instead
        // is what cannot be a grunt type under any upstream: control characters, and a value longer than the
        // column. See #611.
        if (!string.IsNullOrEmpty(model.GruntType))
        {
            if (model.GruntType.Any(char.IsControl))
            {
                throw new AlarmValidationException("gruntType must not contain control characters.");
            }

            if (model.GruntType.Length > MaxGruntTypeLength)
            {
                throw new AlarmValidationException(
                    $"gruntType must be {MaxGruntTypeLength} characters or fewer.");
            }
        }

        if (string.IsNullOrWhiteSpace(model.GruntType))
        {
            // AlarmValidationException rather than ArgumentException: nothing maps the latter, so this
            // message -- written precisely to explain the problem -- came back as a bare 500 on the
            // update path while the create path answered 400. See #518.
            throw new AlarmValidationException(
                "grunt_type is required — PoracleNG has no catch-all value. To track everything, "
                + "create one alarm per InvasionGruntTypes.All entry.");
        }
    }

    private static List<Invasion> DeserializeItems(JsonElement json) =>
        PoracleJsonHelper.DeserializeList<Invasion>(json);

    private static JsonElement SerializeToElement<T>(T value) =>
        PoracleJsonHelper.SerializeToElement(value);
}
