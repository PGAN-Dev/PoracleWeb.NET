using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Tracking creates, edits and bulk settings, over PoracleNG's <c>/api/v2</c> surface only.
/// </summary>
public partial class PoracleTrackingProxy
{
    /// <summary>
    /// What a user is told when a write would make another alarm of the same type take on these settings,
    /// whether PoracleNG would merge it (create) or refuses it as a duplicate (replace).
    /// </summary>
    internal const string TakeoverMessage =
        "Another alarm of this type already uses those settings. Edit or remove that one instead.";

    private const string DryRunQuery = "&dry_run=true";

    /// <inheritdoc />
    public Task<TrackingCreateResult> CreateAsync(string type, string userId, JsonElement body) =>
        this.CreateCoreAsync(type, userId, body, profileNo: null);

    /// <inheritdoc />
    public Task<TrackingCreateResult> CreateOnProfileAsync(string type, string userId, JsonElement body, int profileNo) =>
        this.CreateCoreAsync(type, userId, body, profileNo);

    /// <inheritdoc />
    public async Task<TrackingUpdateResult> UpdateByUidAsync(string type, string userId, int uid, JsonElement body)
    {
        // uid 0 is an insert, which v2's uid-addressed PUT cannot express.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(uid);
        body = OverrideScope.WithoutBlankAreas(body);

        var translated = await this.TranslateAllAsync(type, [body]);
        var trusted = await this.AuthoriseOverrideAreasAsync(
            userId, [body], () => this.StoredOverrideAreasAsync(type, userId, uid));

        return await this.PutV2Async(type, userId, uid, translated[0], trusted);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A refusal of one rule is in the answer, not an exception: PATCH has no all-or-nothing. A failure of the
    /// whole request maps as every other v2 write does (<see cref="ThrowForFailedWrite"/>).
    /// </remarks>
    public async Task<TrackingPatchResult> PatchAsync(
        string type, string userId, IReadOnlyList<int> uids, TrackingPatchSet settings)
    {
        if (uids.Count == 0)
        {
            return TrackingPatchResult.None;
        }

        var bodyText = TrackingPatchWire.Body(uids, settings);
        LogPatchRequest(this._logger, type, userId, bodyText);

        // Always trusted. PoracleNG re-validates the whole stored rule on a PATCH, and untrusted its area check
        // allows only userSelectable fences, which a user's own drawn fence never is: every rule limited to one
        // came back refused as invalid, so a clean toggle silently did nothing on it. Trusting the write grants
        // nothing new, because a PATCH set has no override_areas: the only areas checked are the ones already
        // stored on the rule, which the area policy allows anyway.
        var request = this.CreateRequest(
            HttpMethod.Patch,
            $"{this._apiAddress}/api/v2/humans/{Encode(userId)}/tracking/{type}?silent=true{TrustedQuery(trusted: true)}");
        request.Content = new StringContent(bodyText, Encoding.UTF8, "application/json");

        var response = await this._httpClient.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            ThrowForFailedWrite(response, type, payload);
        }

        LogPatchResponse(this._logger, type, userId, payload);
        return TrackingPatchWire.Parse(payload);
    }

    /// <summary>
    /// The create protocol: a dry run of every row, then a commit of only the rows the dry run reports as new.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v2's POST still diffs each row against the stored rules: identical is <c>unchanged</c>, one updatable
    /// field away is <c>updated</c> (the stored rule takes the new value, which is the takeover #561 was), and
    /// anything else is <c>created</c>. A single row that would update is refused; one that is unchanged is
    /// reported as already present. A batch writes the rows that are new and counts the rest.
    /// </para>
    /// <para>
    /// The answer's buckets carry no index back to the request, so a mixed batch asks about each row alone.
    /// That is sound because PoracleNG classifies against stored rules only, never between the rows of one
    /// batch (<c>store.RowDiffPolicy.Classify</c>). Inserts come back in submission order, which is how a
    /// committed uid is paired with its row.
    /// </para>
    /// <para>
    /// Between the dry run and the commit another write can make a new row a takeover after all. The commit
    /// then reports it under <c>updated</c> with the rule's <c>previous</c> state, which is put back before
    /// the row is refused or counted.
    /// </para>
    /// </remarks>
    private async Task<TrackingCreateResult> CreateCoreAsync(string type, string userId, JsonElement body, int? profileNo)
    {
        var single = body.ValueKind != JsonValueKind.Array;
        List<JsonElement> rows = single ? [body] : [.. body.EnumerateArray()];
        rows = [.. rows.Select(OverrideScope.WithoutBlankAreas)];
        if (rows.Count == 0)
        {
            return new TrackingCreateResult([], 0, 0, 0, []);
        }

        var translated = await this.TranslateAllAsync(type, rows);
        var trusted = await this.AuthoriseOverrideAreasAsync(userId, rows, storedOnRule: null);
        var url = $"{this._apiAddress}/api/v2/humans/{Encode(userId)}/tracking/{type}"
            + $"?silent=true{ProfileQuery(profileNo)}{TrustedQuery(trusted)}";

        var rowUids = new int[rows.Count];
        var alreadyPresent = 0;
        var takeovers = 0;

        var dry = await this.PostRulesAsync(type, userId, url + DryRunQuery, translated);
        List<int> keep;

        if (dry.Updated.Count == 0 && dry.Unchanged.Count == 0)
        {
            keep = [.. Enumerable.Range(0, rows.Count)];
        }
        else if (single)
        {
            if (dry.Updated.Count > 0)
            {
                throw new TrackingConflictException(type, TakeoverMessage) { ExistingUid = ConflictingUidOf(dry.Updated[0]) };
            }

            // An identical rule is already stored; nothing is written.
            var existing = UidOf(dry.Unchanged[0]);
            return new TrackingCreateResult([], 1, 0, 0, [0]) { ExistingUid = existing > 0 ? existing : null };
        }
        else if (dry.Created.Count == 0)
        {
            // Nothing in the batch is new, so nothing would be written and no row needs telling apart. Each row
            // lands in exactly one bucket, so the buckets are the counts. Re-applying a large quick pick used to
            // ask about every row alone here: one POST per row.
            LogCreateSummary(this._logger, type, userId, rows.Count, 0, dry.Unchanged.Count, dry.Updated.Count);
            return new TrackingCreateResult([], dry.Unchanged.Count, 0, 0, rowUids, dry.Updated.Count);
        }
        else
        {
            keep = [];
            for (var i = 0; i < rows.Count; i++)
            {
                // One row's batch answer already is its own classification.
                var verdict = rows.Count == 1
                    ? dry.Verdict
                    : (await this.PostRulesAsync(type, userId, url + DryRunQuery, [translated[i]])).Verdict;

                switch (verdict)
                {
                    case DryRunVerdict.Unchanged:
                        alreadyPresent++;
                        break;

                    case DryRunVerdict.Updated:
                        takeovers++;
                        LogTakeoverSkipped(this._logger, type, userId, i);
                        break;

                    default:
                        keep.Add(i);
                        break;
                }
            }

            if (keep.Count == 0)
            {
                LogCreateSummary(this._logger, type, userId, rows.Count, 0, alreadyPresent, takeovers);
                return new TrackingCreateResult([], alreadyPresent, 0, 0, rowUids, takeovers);
            }
        }

        var commit = await this.PostRulesAsync(type, userId, url, [.. keep.Select(i => translated[i])]);
        alreadyPresent += commit.Unchanged.Count;

        foreach (var takenOver in commit.Updated)
        {
            await this.RestorePreviousAsync(type, userId, takenOver);
            takeovers++;
            LogTakeoverSkipped(this._logger, type, userId, -1);
        }

        if (single && commit.Updated.Count > 0)
        {
            throw new TrackingConflictException(type, TakeoverMessage) { ExistingUid = ConflictingUidOf(commit.Updated[0]) };
        }

        var created = commit.Created.Select(UidOf).ToList();
        if (created.Count == keep.Count)
        {
            for (var k = 0; k < keep.Count; k++)
            {
                rowUids[keep[k]] = created[k];
            }
        }
        else if (created.Count > 0 || commit.Unchanged.Count + commit.Updated.Count != keep.Count)
        {
            // Nothing created and every kept row accounted for (a single create that a concurrent write made
            // identical, say) leaves nothing to pair, so there is nothing to warn about.
            LogCreateUnpaired(this._logger, type, userId, keep.Count, created.Count);
        }

        LogCreateSummary(this._logger, type, userId, rows.Count, created.Count, alreadyPresent, takeovers);
        return new TrackingCreateResult(
            [.. created.Select(uid => (long)uid)], alreadyPresent, 0, created.Count, rowUids, takeovers);
    }

    /// <summary>
    /// Checks every row's scope, then translates every row to its v2 rule, before anything is sent. A row v2
    /// cannot be told faithfully is refused: there is no v1 write to fall back to, and dropping the field
    /// would lose what the user set (#730).
    /// </summary>
    private async Task<List<JsonElement>> TranslateAllAsync(string type, List<JsonElement> rows)
    {
        foreach (var row in rows)
        {
            OverrideScope.EnsureCoherent(row);
        }

        var (bounds, declared) = await this.ServerRuleSchemaAsync(type);
        var translated = new List<JsonElement>(rows.Count);

        foreach (var row in rows)
        {
            if (!TrackingV2Translator.TryTranslate(type, row, bounds, declared, out var rule, out var unsupported))
            {
                throw new AlarmValidationException(
                    $"PoracleNG can't store this alarm as it is: {unsupported ?? "unknown"}. Nothing was changed.");
            }

            translated.Add(rule);
        }

        return translated;
    }

    /// <summary>Authorises every override area the rows name, through the one area policy.</summary>
    /// <returns>
    /// True when any row names one, so the write is sent <c>trusted</c>: PoracleNG's own check allows only
    /// <c>userSelectable</c> areas, which a user's own drawn fence is not.
    /// </returns>
    private async Task<bool> AuthoriseOverrideAreasAsync(
        string userId, List<JsonElement> rows, Func<Task<IReadOnlyCollection<string>>>? storedOnRule)
    {
        var requested = rows
            .SelectMany(OverrideScope.AreasOf)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (requested.Count == 0)
        {
            return false;
        }

        await this._areaPolicy.EnsureOverrideAreasAllowedAsync(userId, requested, storedOnRule);
        return true;
    }

    /// <summary>The override areas the stored rule holds, read through v2's single-rule route.</summary>
    private async Task<IReadOnlyCollection<string>> StoredOverrideAreasAsync(string type, string userId, int uid)
    {
        var request = this.CreateRequest(
            HttpMethod.Get, $"{this._apiAddress}/api/v2/humans/{Encode(userId)}/tracking/{type}/{uid}");
        var response = await this._httpClient.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            ThrowForFailedWrite(response, type, payload);
        }

        using var document = JsonDocument.Parse(payload);
        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("rules", out var rules)
            && rules.ValueKind == JsonValueKind.Array
                ? [.. rules.EnumerateArray().SelectMany(OverrideScope.AreasOf)]
                : [];
    }

    private async Task<WriteEnvelope> PostRulesAsync(string type, string userId, string url, List<JsonElement> rules)
    {
        var bodyText = JsonSerializer.Serialize(rules);
        LogV2CreateRequest(this._logger, type, userId, url, bodyText);

        var request = this.CreateRequest(HttpMethod.Post, url);
        request.Content = new StringContent(bodyText, Encoding.UTF8, "application/json");

        var response = await this._httpClient.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            ThrowForFailedWrite(response, type, payload);
        }

        LogV2CreateResponse(this._logger, type, userId, payload);
        return WriteEnvelope.Parse(payload);
    }

    /// <summary>
    /// Puts back a rule a commit took over, from the <c>previous</c> state PoracleNG reported for it. Trusted,
    /// because the areas it held were already stored.
    /// </summary>
    private async Task RestorePreviousAsync(string type, string userId, JsonElement takenOver)
    {
        var uid = UidOf(takenOver);
        if (!takenOver.TryGetProperty("previous", out var previous) || previous.ValueKind != JsonValueKind.Object)
        {
            LogRestoreImpossible(this._logger, type, uid, userId, "no previous state was reported", takenOver.GetRawText());
            return;
        }

        if (uid <= 0)
        {
            uid = UidOf(previous);
        }

        JsonElement row;
        try
        {
            row = TrackingV2Translator.ToV1Row(type, previous, userId);
        }
        catch (InvalidOperationException ex)
        {
            LogRestoreImpossible(this._logger, type, uid, userId, ex.Message, previous.GetRawText());
            return;
        }

        if (uid <= 0)
        {
            LogRestoreImpossible(this._logger, type, uid, userId, "no uid was reported", previous.GetRawText());
            return;
        }

        var (bounds, declared) = await this.ServerRuleSchemaAsync(type);
        if (!TrackingV2Translator.TryTranslate(type, row, bounds, declared, out var rule, out var unsupported))
        {
            LogRestoreImpossible(this._logger, type, uid, userId, unsupported ?? "unknown", previous.GetRawText());
            return;
        }

        await this.PutV2Async(type, userId, uid, rule, trusted: true);
    }

    /// <summary>How a v2 dry run classified a single row.</summary>
    internal enum DryRunVerdict
    {
        Created,
        Updated,
        Unchanged,
    }

    /// <summary>
    /// The <c>{dry_run, created, updated, unchanged}</c> answer to a v2 tracking create or replace. Each bucket
    /// is a list of rules carrying <c>uid</c> (0 on a dry-run create); an <c>updated</c> rule also carries
    /// <c>previous</c>. A null bucket is an empty one.
    /// </summary>
    internal sealed record WriteEnvelope(
        IReadOnlyList<JsonElement> Created,
        IReadOnlyList<JsonElement> Updated,
        IReadOnlyList<JsonElement> Unchanged)
    {
        /// <summary>
        /// What a dry run of ONE row says about it. A takeover outranks everything, then an exact duplicate;
        /// an answer that says nothing is left for the commit, which repairs a takeover it did not foresee.
        /// </summary>
        public DryRunVerdict Verdict =>
            this.Updated.Count > 0 ? DryRunVerdict.Updated
            : this.Unchanged.Count > 0 && this.Created.Count == 0 ? DryRunVerdict.Unchanged
            : DryRunVerdict.Created;

        public static WriteEnvelope Parse(string payload)
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            IReadOnlyList<JsonElement> Bucket(string name) =>
                root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(name, out var bucket)
                && bucket.ValueKind == JsonValueKind.Array
                    ? [.. bucket.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object).Select(r => r.Clone())]
                    : [];

            return new WriteEnvelope(Bucket("created"), Bucket("updated"), Bucket("unchanged"));
        }
    }

    /// <summary>
    /// The numeric limits THIS server declares for its own v2 rules, and every property each rule declares,
    /// read from the <c>/openapi.json</c> it publishes and cached for ten minutes.
    /// </summary>
    /// <remarks>
    /// Read from the server rather than shipped as a table because the two disagree. PoracleNG bounds all
    /// 55 numeric filter fields on an unreleased branch, and this site stores values outside ten of them --
    /// values PoracleNG itself wrote. Applying those limits to a server that does not enforce them was
    /// measured at 68% of one instance's Pokemon rules dropping off the v2 write path, for no benefit,
    /// because that server accepts every one of the values. A 5.2.1 publishes exactly one bound.
    /// <para>
    /// Unreachable or unparseable yields no bounds rather than all bounds, so a failed fetch leaves the
    /// write exactly as it is today and lets v2 answer for itself. It also yields no declared properties,
    /// so nothing the field tables do not model is carried through on the strength of a guess.
    /// </para>
    /// </remarks>
    private async Task<(IReadOnlyDictionary<string, TrackingV2Translator.Bound>? Bounds, IReadOnlySet<string>? Declared)>
        ServerRuleSchemaAsync(string type)
    {
        if (!this._cache.TryGetValue<ServerRuleSchemas>(V2BoundsCacheKey, out var schemas) || schemas is null)
        {
            string? document = null;

            try
            {
                document = await this._httpClient.GetStringAsync($"{this._apiAddress}/openapi.json");
            }
            catch (HttpRequestException exception)
            {
                LogV2BoundsUnavailable(this._logger, exception.Message);
            }
            catch (TaskCanceledException exception)
            {
                LogV2BoundsUnavailable(this._logger, exception.Message);
            }

            schemas = new ServerRuleSchemas(V2SchemaBounds.Parse(document), V2SchemaBounds.ParseDeclared(document));
            this._cache.Set(V2BoundsCacheKey, schemas, V2BoundsFor);
        }

        return (
            schemas.Bounds.TryGetValue(type, out var bounds) ? bounds : null,
            schemas.Declared.TryGetValue(type, out var declared) ? declared : null);
    }

    private sealed record ServerRuleSchemas(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, TrackingV2Translator.Bound>> Bounds,
        IReadOnlyDictionary<string, IReadOnlySet<string>> Declared);

    /// <summary>Full-replaces one rule through <c>/api/v2</c>; <paramref name="body"/> is already a v2 rule.</summary>
    private async Task<TrackingUpdateResult> PutV2Async(
        string type, string userId, int uid, JsonElement body, bool trusted)
    {
        var bodyText = body.GetRawText();
        LogV2UpdateRequest(this._logger, type, uid, userId, bodyText);

        var request = this.CreateRequest(
            HttpMethod.Put,
            $"{this._apiAddress}/api/v2/humans/{Encode(userId)}/tracking/{type}/{uid}?silent=true{TrustedQuery(trusted)}");
        request.Content = new StringContent(bodyText, Encoding.UTF8, "application/json");

        var response = await this._httpClient.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            ThrowForFailedWrite(response, type, payload);
        }

        LogV2UpdateResponse(this._logger, type, uid, userId, payload);
        return new TrackingUpdateResult(UidFromV2Envelope(payload) ?? uid, true);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Always trusted and never run through <see cref="IUserAreaPolicy"/>: the row came from PoracleNG, so the
    /// areas on it are ones it already stored, the way the takeover restore sends them. A dry run reports what
    /// the replace would do and writes nothing.
    /// </remarks>
    public async Task<StoredRuleReplaceOutcome> ReplaceStoredRuleAsync(
        string type, string userId, int uid, int profileNo, JsonElement v1Row, bool dryRun)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(uid);

        var (bounds, declared) = await this.ServerRuleSchemaAsync(type);
        if (!TrackingV2Translator.TryTranslate(type, v1Row, bounds, declared, out var rule, out var unsupported))
        {
            throw new AlarmValidationException(
                $"PoracleNG can't store this alarm as it is: {unsupported ?? "unknown"}. Nothing was changed.");
        }

        var bodyText = rule.GetRawText();
        LogV2UpdateRequest(this._logger, type, uid, userId, bodyText);

        var request = this.CreateRequest(
            HttpMethod.Put,
            $"{this._apiAddress}/api/v2/humans/{Encode(userId)}/tracking/{type}/{uid}"
            + $"?silent=true&trusted=true&profile={profileNo}{(dryRun ? "&dry_run=true" : string.Empty)}");
        request.Content = new StringContent(bodyText, Encoding.UTF8, "application/json");

        var response = await this._httpClient.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            try
            {
                ThrowForFailedWrite(response, type, payload);
            }
            catch (TrackingConflictException)
            {
                return StoredRuleReplaceOutcome.Conflict;
            }
            catch (TrackingRuleNotFoundException)
            {
                return StoredRuleReplaceOutcome.NotFound;
            }
        }

        LogV2UpdateResponse(this._logger, type, uid, userId, payload);

        // Only an explicit answer counts. An empty or unreadable 200 must not read as "go ahead and write".
        WriteEnvelope envelope;
        try
        {
            envelope = WriteEnvelope.Parse(payload);
        }
        catch (JsonException)
        {
            throw AmbiguousReplaceAnswer(type, uid, payload);
        }

        // A replace never creates; a created rule beside the answer means this is not the answer we asked for.
        return envelope.Created.Count > 0 ? throw AmbiguousReplaceAnswer(type, uid, payload)
            : envelope.Updated.Count > 0 ? StoredRuleReplaceOutcome.Updated
            : envelope.Unchanged.Count > 0 ? StoredRuleReplaceOutcome.Unchanged
            : throw AmbiguousReplaceAnswer(type, uid, payload);
    }

    private static PoracleAmbiguousAnswerException AmbiguousReplaceAnswer(string type, int uid, string payload) =>
        new($"PoracleNG answered the {type} rule {uid} replace with neither an updated nor an unchanged rule: {(payload.Length > 200 ? payload[..200] : payload)}");

    /// <summary>
    /// The stored rule a would-be takeover names: <c>previous</c> is that rule as it stands, so its uid is
    /// the one to edit; the updated entry's own uid is the fallback. Null when neither carries one.
    /// </summary>
    internal static int? ConflictingUidOf(JsonElement updated)
    {
        var uid = updated.TryGetProperty("previous", out var previous) && previous.ValueKind == JsonValueKind.Object
            ? UidOf(previous)
            : 0;
        if (uid <= 0)
        {
            uid = UidOf(updated);
        }

        return uid > 0 ? uid : null;
    }

    private static string TrustedQuery(bool trusted) => trusted ? "&trusted=true" : string.Empty;

    /// <summary>Turns a failed v2 create or replace into the exception the rest of the app understands.</summary>
    private static void ThrowForFailedWrite(HttpResponseMessage response, string type, string payload)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound when !PoracleProblemDetails.IsProblemJson(payload):
                // gin's plaintext "404 page not found": the route is absent, so this is not 5.4.0.
                throw new PoracleUnsupportedException("saving alarms", RequiredVersion);

            case HttpStatusCode.NotFound when payload.Contains("human not found", StringComparison.Ordinal):
                // Same meaning as the bare 404 on a tracking read: the account is gone.
                throw new AccountGoneException();

            case HttpStatusCode.NotFound:
                throw new TrackingRuleNotFoundException(type, PoracleProblemDetails.Describe(payload));

            case HttpStatusCode.Conflict:
                // v2 refuses a replacement that would duplicate another rule, exactly or one updatable field
                // away. To the user that is the same thing as a takeover: another alarm already holds those settings.
                throw new TrackingConflictException(type, TakeoverMessage);

            case HttpStatusCode.UnprocessableEntity:
            case HttpStatusCode.BadRequest:
                // v2 validates at 422 where v1 used 400. Both are the request being wrong, not the server;
                // letting EnsureSuccessStatusCode throw would flatten them into an opaque 500. See #539.
                throw new AlarmValidationException(PoracleProblemDetails.Describe(payload));

            default:
                response.EnsureSuccessStatusCode();
                return;
        }
    }

    /// <summary>
    /// The uid the replacement lives under. v2 answers <c>{created, updated, unchanged}</c> and a PUT
    /// reports under <c>updated</c> -- but reading all three costs nothing and stops a future shuffle
    /// silently returning the dead uid.
    /// </summary>
    private static int? UidFromV2Envelope(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var name in new[] { "updated", "created", "unchanged" })
            {
                if (!root.TryGetProperty(name, out var bucket) || bucket.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var rule in bucket.EnumerateArray())
                {
                    if (rule.ValueKind == JsonValueKind.Object
                        && rule.TryGetProperty("uid", out var value)
                        && value.ValueKind == JsonValueKind.Number
                        && value.TryGetInt32(out var found)
                        && found > 0)
                    {
                        return found;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // A 200 that is not the envelope. The rule was written; we simply do not know its uid, and the
            // caller keeps the one it had rather than losing the edit.
        }

        return null;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "v2 update {Type} uid={Uid} for {UserId} request: {Body}")]
    private static partial void LogV2UpdateRequest(ILogger logger, string type, int uid, string userId, string body);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "v2 update {Type} uid={Uid} for {UserId} response: {Response}")]
    private static partial void LogV2UpdateResponse(ILogger logger, string type, int uid, string userId, string response);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Could not read PoracleNG's /openapi.json ({Reason}); treating its v2 rules as unbounded, which is how they are sent today.")]
    private static partial void LogV2BoundsUnavailable(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "v2 patch {Type} for {UserId} request: {Body}")]
    private static partial void LogPatchRequest(ILogger logger, string type, string userId, string body);

    [LoggerMessage(Level = LogLevel.Information, Message = "v2 patch {Type} for {UserId} response: {Response}")]
    private static partial void LogPatchResponse(ILogger logger, string type, string userId, string response);

    // Debug, not Information: a create of a large batch posts its rows more than once, and every body logged in
    // full at Information filled the log. LogCreateSummary is the Information line.
    [LoggerMessage(Level = LogLevel.Debug, Message = "v2 create {Type} for {UserId} request to {Url}: {Body}")]
    private static partial void LogV2CreateRequest(ILogger logger, string type, string userId, string url, string body);

    [LoggerMessage(Level = LogLevel.Debug, Message = "v2 create {Type} for {UserId} response: {Response}")]
    private static partial void LogV2CreateResponse(ILogger logger, string type, string userId, string response);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "v2 create {Type} for {UserId}: {Rows} rows, {Created} created, {AlreadyPresent} already present, {Takeovers} left out as takeovers")]
    private static partial void LogCreateSummary(
        ILogger logger, string type, string userId, int rows, int created, int alreadyPresent, int takeovers);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Create {Type} for {UserId}: left out row {Row} (-1: found at the commit and put back), which would have taken another alarm over.")]
    private static partial void LogTakeoverSkipped(ILogger logger, string type, string userId, int row);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Create {Type} for {UserId}: committed {Kept} rows but PoracleNG created {Created}, so no created uid can be paired with its row.")]
    private static partial void LogCreateUnpaired(ILogger logger, string type, string userId, int kept, int created);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Create {Type} for {UserId} took over uid={Uid} and it could not be put back ({Reason}). Its previous state: {Previous}")]
    private static partial void LogRestoreImpossible(ILogger logger, string type, int uid, string userId, string reason, string previous);
}

