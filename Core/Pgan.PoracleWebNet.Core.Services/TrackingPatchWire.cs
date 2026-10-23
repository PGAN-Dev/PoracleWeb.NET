using System.Text.Json;
using System.Text.Json.Nodes;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// The wire form of PoracleNG's v2 tracking <c>PATCH</c>, shared by the tracking and incident proxies: a
/// <c>{"uids":[...],"set":{...}}</c> body and a <c>{dry_run, updated, unchanged, refused}</c> answer.
/// </summary>
internal static class TrackingPatchWire
{
    /// <summary>
    /// The request body. Only the settings that are not null are named, because PoracleNG leaves every field
    /// the set does not name as stored, and <c>V2PatchSet</c> is <c>additionalProperties: false</c>.
    /// </summary>
    public static string Body(IReadOnlyList<int> uids, TrackingPatchSet settings)
    {
        var named = new JsonObject();
        if (settings.Distance is { } distance)
        {
            named["distance"] = distance;
        }

        if (settings.Clean is { } clean)
        {
            named["clean"] = clean;
        }

        if (named.Count == 0)
        {
            // PoracleNG requires at least one setting; a set naming none is a caller bug, not a no-op.
            throw new ArgumentException("A PATCH must name at least one setting.", nameof(settings));
        }

        var body = new JsonObject
        {
            ["uids"] = new JsonArray([.. uids.Select(uid => (JsonNode)uid)]),
            ["set"] = named,
        };

        return body.ToJsonString();
    }

    /// <summary>Reads the answer. A null or absent list is an empty one.</summary>
    public static TrackingPatchResult Parse(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            return TrackingPatchResult.None;
        }

        return new TrackingPatchResult(Uids(root, "updated"), Uids(root, "unchanged"), Refusals(root));
    }

    private static List<int> Uids(JsonElement root, string name) =>
        root.TryGetProperty(name, out var bucket) && bucket.ValueKind == JsonValueKind.Array
            ? [.. bucket.EnumerateArray().Select(UidOf).Where(uid => uid > 0)]
            : [];

    private static List<PatchRefusal> Refusals(JsonElement root)
    {
        if (!root.TryGetProperty("refused", out var refused) || refused.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. refused.EnumerateArray()
                .Where(r => r.ValueKind == JsonValueKind.Object)
                .Select(r => new PatchRefusal(
                    UidOf(r),
                    Text(r, "reason") ?? string.Empty,
                    Text(r, "detail") ?? string.Empty,
                    r.TryGetProperty("conflicts_with", out var with) && with.ValueKind == JsonValueKind.Number
                        && with.TryGetInt32(out var other)
                            ? other
                            : null)),
        ];
    }

    private static int UidOf(JsonElement rule) =>
        rule.ValueKind == JsonValueKind.Object
        && rule.TryGetProperty("uid", out var uid)
        && uid.ValueKind == JsonValueKind.Number
        && uid.TryGetInt32(out var value)
            ? value
            : 0;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
