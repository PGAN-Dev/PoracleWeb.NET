using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Tracking reads, over PoracleNG's <c>/api/v2</c> surface and restated in the v1 shape every caller was
/// written against (see <see cref="TrackingV2Translator.ToV1Row"/>). Nothing here reads a v1 route.
/// </summary>
public partial class PoracleTrackingProxy
{
    /// <summary>The keys v1's all-tracking read answered, which is what every consumer indexes by.</summary>
    private static readonly string[] SnapshotTypes =
        ["egg", "fort", "gym", "invasion", "lure", "maxbattle", "nest", "pokemon", "quest", "raid"];

    private const string RequiredVersion = "PoracleNG 5.4.0";

    /// <inheritdoc />
    /// <remarks>
    /// <c>invasion</c> reads the <c>incident</c> list as well and folds it in: v1's invasion list held the
    /// Pokestop events (<c>kecleon</c>, <c>gold-stop</c>, <c>showcase</c>) as rows whose <c>grunt_type</c> is
    /// the event name, and every caller was written against that.
    /// </remarks>
    public async Task<JsonElement> GetByUserAsync(string type, string userId)
    {
        if (type != "incident" && !SnapshotTypes.Contains(type))
        {
            throw new ArgumentException($"'{type}' is not a PoracleNG tracking type.", nameof(type));
        }

        var rules = await this.ReadRulesAsync(type, userId);
        var rows = this.V1Rows(type, rules, userId);

        if (type == "invasion")
        {
            var events = await this.ReadRulesAsync("incident", userId);
            rows.AddRange(this.EventRowsAsInvasions(this.V1Rows("incident", events, userId)));
        }

        return JsonSerializer.SerializeToElement(rows);
    }

    /// <inheritdoc />
    public async Task<JsonElement> GetAllTrackingAsync(string userId)
    {
        var snapshot = await this.ReadSnapshotAsync(userId, string.Empty, "All alarms");
        return this.SnapshotAsV1(snapshot, userId, profiles: null, appended: null);
    }

    /// <inheritdoc />
    /// <remarks>
    /// PoracleNG answers <c>?all_profiles=true</c> with nothing at all for a human who has no
    /// <c>profiles</c> rows, though their rules exist (v1's <c>allProfiles</c> returned them). Losing them
    /// here would blank the overview and let a duplicate copy nothing, so that case reads the
    /// active-profile snapshot instead and stamps each rule with the human's current profile number.
    /// </remarks>
    public async Task<JsonElement> GetAllTrackingAllProfilesAsync(string userId)
    {
        var snapshot = await this.ReadSnapshotAsync(userId, "?all_profiles=true&include_descriptions=true", "All alarms");

        var profiles = ProfilesOf(snapshot);
        var current = CurrentProfileNo(snapshot);

        if (current is not null && ListsProfile(profiles, current.Value))
        {
            return this.SnapshotAsV1(snapshot, userId, profiles, appended: null);
        }

        var active = await this.ReadSnapshotAsync(userId, "?include_descriptions=true", "All alarms");
        var activeNo = CurrentProfileNo(active) ?? current ?? 0;

        // The active profile has no profiles row (so all_profiles skipped it), unless the active read
        // says otherwise: then its rules are already in the first answer and must not be added twice.
        return ListsProfile(profiles, activeNo)
            ? this.SnapshotAsV1(snapshot, userId, profiles, appended: null)
            : this.SnapshotAsV1(snapshot, userId, profiles, (active, activeNo));
    }

    /// <inheritdoc />
    /// <remarks>
    /// The same snapshot and the same gap as <see cref="GetAllTrackingAllProfilesAsync"/> (a human whose active
    /// profile has no <c>profiles</c> row is missing from <c>all_profiles</c>), but the rules come back as v2
    /// sent them, <c>incident</c> included as its own type. Without descriptions: nothing reads them here, and
    /// rendering one for every rule of every human is work PoracleNG need not do during a scan.
    /// </remarks>
    public async Task<IReadOnlyList<StoredRule>> GetStoredRulesAsync(string userId)
    {
        var snapshot = await this.ReadSnapshotAsync(userId, "?all_profiles=true", "All alarms");

        var profiles = ProfilesOf(snapshot);
        var current = CurrentProfileNo(snapshot);

        var rules = new List<StoredRule>();
        this.CollectStoredRules(snapshot, stampProfileNo: null, rules);

        if (current is not null && ListsProfile(profiles, current.Value))
        {
            return rules;
        }

        var active = await this.ReadSnapshotAsync(userId, string.Empty, "All alarms");
        var activeNo = CurrentProfileNo(active) ?? current ?? 0;

        if (!ListsProfile(profiles, activeNo))
        {
            // Since jfberry/PoracleNG#331 the all-profiles snapshot already carries these rules too, profile_no
            // and all. Older servers still need this read, so keep it and add only what is not collected yet.
            // uids are per table: the key is the type and the uid together.
            var seen = rules.Select(r => (r.Type, r.Uid)).ToHashSet();
            var fromActive = new List<StoredRule>();
            this.CollectStoredRules(active, activeNo, fromActive);
            rules.AddRange(fromActive.Where(r => seen.Add((r.Type, r.Uid))));
        }

        return rules;
    }

    private void CollectStoredRules(JsonElement snapshot, int? stampProfileNo, List<StoredRule> into)
    {
        if (snapshot.ValueKind != JsonValueKind.Object
            || !snapshot.TryGetProperty("tracking", out var tracking)
            || tracking.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var type in SnapshotTypes.Append("incident"))
        {
            if (!tracking.TryGetProperty(type, out var list) || list.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var rule in list.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object))
            {
                var profileNo = stampProfileNo
                    ?? (rule.TryGetProperty("profile_no", out var no) && no.ValueKind == JsonValueKind.Number && no.TryGetInt32(out var found)
                        ? found
                        : (int?)null);

                if (profileNo is null)
                {
                    // A repair would PUT to a profile we only guessed at.
                    LogRuleWithoutProfile(this._logger, type, UidOf(rule));
                    continue;
                }

                into.Add(new StoredRule(type, profileNo.Value, UidOf(rule), rule.Clone()));
            }
        }
    }

    private static JsonElement ProfilesOf(JsonElement snapshot) =>
        snapshot.ValueKind == JsonValueKind.Object
        && snapshot.TryGetProperty("profiles", out var profiles)
        && profiles.ValueKind == JsonValueKind.Array
            ? profiles
            : PoracleJsonHelper.EmptyArray;

    private static bool ListsProfile(JsonElement profiles, int profileNo) =>
        profiles.EnumerateArray().Any(p =>
            p.ValueKind == JsonValueKind.Object
            && p.TryGetProperty("profile_no", out var no)
            && no.ValueKind == JsonValueKind.Number
            && no.GetInt32() == profileNo);

    private static int? CurrentProfileNo(JsonElement snapshot) =>
        snapshot.TryGetProperty("human", out var human)
        && human.ValueKind == JsonValueKind.Object
        && human.TryGetProperty("current_profile_no", out var no)
        && no.ValueKind == JsonValueKind.Number
            ? no.GetInt32()
            : null;

    private async Task<JsonElement> ReadRulesAsync(string type, string userId)
    {
        var root = await this.GetV2Async(
            $"{this._apiAddress}/api/v2/humans/{Encode(userId)}/tracking/{type}?include_descriptions=true", "Alarm list");

        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("rules", out var rules) ? rules : default;
    }

    private async Task<JsonElement> ReadSnapshotAsync(string userId, string query, string feature) =>
        await this.GetV2Async(
            $"{this._apiAddress}/api/v2/humans/{Encode(userId)}/tracking{query}", feature);

    private async Task<JsonElement> GetV2Async(string url, string feature)
    {
        var request = this.CreateRequest(HttpMethod.Get, url);
        var response = await this._httpClient.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound when !PoracleProblemDetails.IsProblemJson(payload):
                    // gin's plaintext "404 page not found": the route is absent, so this is not 5.4.0.
                    throw new PoracleUnsupportedException(feature, RequiredVersion);

                case HttpStatusCode.NotFound when payload.Contains("human not found", StringComparison.Ordinal):
                    throw new AccountGoneException();

                default:
                    response.EnsureSuccessStatusCode();
                    break;
            }
        }

        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// A list of v2 rules as v1 rows. A rule that cannot be translated (an enum value this build does not
    /// know) is logged and left out rather than failing the list: one rule written by a newer PoracleNG
    /// must not blank the dashboard, the cleaning page or an account deletion for everyone who holds it.
    /// </summary>
    private List<JsonElement> V1Rows(string type, JsonElement rules, string userId, int? stampProfileNo = null)
    {
        var rows = new List<JsonElement>();
        if (rules.ValueKind != JsonValueKind.Array)
        {
            return rows;
        }

        foreach (var rule in rules.EnumerateArray())
        {
            if (rule.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            try
            {
                var row = TrackingV2Translator.ToV1Row(type, rule, userId);
                rows.Add(stampProfileNo is { } no && !row.TryGetProperty("profile_no", out _) ? WithProfileNo(row, no) : row);
            }
            catch (InvalidOperationException ex)
            {
                LogUnreadableRule(this._logger, type, UidOf(rule), ex.Message);
            }
        }

        return rows;
    }

    private static JsonElement WithProfileNo(JsonElement row, int profileNo)
    {
        var node = JsonNode.Parse(row.GetRawText())!.AsObject();
        node["profile_no"] = profileNo;
        return JsonSerializer.SerializeToElement(node);
    }

    private static int UidOf(JsonElement rule) =>
        rule.TryGetProperty("uid", out var uid) && uid.ValueKind == JsonValueKind.Number ? uid.GetInt32() : 0;

    /// <summary>
    /// <c>incident</c> rows in the form v1's invasion list held them: <c>grunt_type</c> is the event name
    /// (the mapping PoracleNG itself stores) and <c>gender</c> is 0. An event of a kind this build has no
    /// name for is left out and logged, since a row with no <c>grunt_type</c> could be written back as an
    /// ordinary invasion.
    /// </summary>
    private List<JsonElement> EventRowsAsInvasions(IEnumerable<JsonElement> incidentRows)
    {
        var rows = new List<JsonElement>();

        foreach (var row in incidentRows)
        {
            var name = row.TryGetProperty("display_type", out var type) && type.ValueKind == JsonValueKind.Number
                ? PokestopEventTypes.NameFor(type.GetInt32())
                : null;

            if (name is null)
            {
                LogUnreadableRule(this._logger, "incident", UidOf(row), $"display_type {(row.TryGetProperty("display_type", out var t) ? t.ToString() : "absent")} is not a known Pokestop event");
                continue;
            }

            var node = JsonNode.Parse(row.GetRawText())!.AsObject();
            node.Remove("display_type");
            node["grunt_type"] = name;
            node["gender"] = 0;
            rows.Add(JsonSerializer.SerializeToElement(node));
        }

        return rows;
    }

    /// <summary>
    /// A v2 snapshot as v1's <c>{ "pokemon": [...], ..., "status": "ok" }</c>. Types the snapshot leaves
    /// null come out as empty arrays; <c>incident</c> has no key of its own, its rules sit in
    /// <c>invasion</c>. The <c>profile</c> list is present only when the caller asked for it.
    /// </summary>
    private JsonElement SnapshotAsV1(JsonElement snapshot, string userId, JsonElement? profiles, (JsonElement Snapshot, int ProfileNo)? appended)
    {
        var output = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var type in SnapshotTypes)
        {
            var rows = this.SnapshotRows(snapshot, type, userId, stampProfileNo: null);

            if (appended is { } extra)
            {
                rows.AddRange(this.SnapshotRows(extra.Snapshot, type, userId, extra.ProfileNo));
            }

            output[type] = rows;
        }

        if (profiles is { } supplied)
        {
            output["profile"] = PoracleHumanProxy
                .V1ProfilesFromV2($$"""{"profiles":{{supplied.GetRawText()}}}""")
                .GetProperty("profile");
        }

        output["status"] = "ok";
        return JsonSerializer.SerializeToElement(output);
    }

    /// <summary>One snapshot type as v1 rows; <c>invasion</c> carries the <c>incident</c> rules too.</summary>
    private List<JsonElement> SnapshotRows(JsonElement snapshot, string type, string userId, int? stampProfileNo)
    {
        var tracking = snapshot.ValueKind == JsonValueKind.Object
            && snapshot.TryGetProperty("tracking", out var t)
            && t.ValueKind == JsonValueKind.Object
                ? t
                : default;

        JsonElement Of(string key) =>
            tracking.ValueKind == JsonValueKind.Object && tracking.TryGetProperty(key, out var list) ? list : default;

        var rows = this.V1Rows(type, Of(type), userId, stampProfileNo);

        if (type == "invasion")
        {
            rows.AddRange(this.EventRowsAsInvasions(this.V1Rows("incident", Of("incident"), userId, stampProfileNo)));
        }

        return rows;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Left out {Type} rule uid={Uid} from the stored-rule read: it names no profile_no.")]
    private static partial void LogRuleWithoutProfile(ILogger logger, string type, int uid);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Left out {Type} rule uid={Uid} from a read: it cannot be restated in the v1 shape ({Reason}).")]
    private static partial void LogUnreadableRule(ILogger logger, string type, int uid, string reason);
}
