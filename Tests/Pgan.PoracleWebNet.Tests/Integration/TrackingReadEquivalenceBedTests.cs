using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;
using Pgan.PoracleWebNet.Core.Services;

namespace Pgan.PoracleWebNet.Tests.Integration;

/// <summary>
/// Compares PoracleNG's v1 tracking reads against what <see cref="PoracleTrackingProxy"/> now builds from
/// v2, for every human a test bed holds. Bed-only: it needs a running PoracleNG that still serves the v1
/// routes, so it is skipped unless <c>PWEB_BED_PORACLE</c> is set.
/// </summary>
/// <remarks>
/// <para>Environment:</para>
/// <list type="bullet">
/// <item><c>PWEB_BED_PORACLE</c> — PoracleNG base address, e.g. <c>http://localhost:13046</c> over an SSH tunnel.</item>
/// <item><c>PWEB_BED_SECRET</c> — its <c>X-Poracle-Secret</c>. Never committed.</item>
/// <item><c>PWEB_BED_HUMANS</c> — a file of human ids, one per line.</item>
/// <item><c>PWEB_BED_REPORT</c> — where to write the per-type counts and every difference (ids redacted); defaults to bed-equivalence-report.txt in the temp directory.</item>
/// </list>
/// <para>
/// Differences are allowed only where the v2 read is known to lose information: legacy zeros
/// (<c>pvp_ranking_best</c> 0, <c>pvp_ranking_worst</c> 0, <c>size</c> 0), <c>""</c> against null for
/// <c>gym_id</c>/<c>station_id</c>, invasion gender on the <c>everything</c> and <c>boss</c> catch-alls, the edit bit on
/// types whose message is never edited, the raid/max battle 9000/9000 any-tier rule read as level 90,
/// Pokestop event gender, and <c>profile_no</c> absent from an active-profile read.
/// </para>
/// </remarks>
[Trait("Category", "Bed")]
public class TrackingReadEquivalenceBedTests
{
    private static readonly string[] Types =
        ["pokemon", "raid", "egg", "quest", "invasion", "lure", "nest", "gym", "maxbattle", "fort"];

    private static readonly HashSet<string> EditBitDroppedTypes = ["gym", "quest", "nest", "maxbattle", "invasion"];

    private static readonly HashSet<string> BossGrunts = ["everything", "boss"];

    [BedFact]
    public async Task V2ReadsRestateV1ForEveryHumanOnTheBed()
    {
        var address = Environment.GetEnvironmentVariable("PWEB_BED_PORACLE")!.TrimEnd('/');
        var secret = Environment.GetEnvironmentVariable("PWEB_BED_SECRET")
            ?? throw new InvalidOperationException("PWEB_BED_SECRET is not set.");
        var humans = File.ReadAllLines(Environment.GetEnvironmentVariable("PWEB_BED_HUMANS")
            ?? throw new InvalidOperationException("PWEB_BED_HUMANS is not set."))
            .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var reportPath = Environment.GetEnvironmentVariable("PWEB_BED_REPORT") ?? Path.Combine(Path.GetTempPath(), "bed-equivalence-report.txt");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var proxy = CreateProxy(http, address, secret);

        var tally = new Tally();
        var index = 0;
        var redaction = humans.ToDictionary(h => h, _ => $"h{Interlocked.Increment(ref index):D4}");

        await Parallel.ForEachAsync(humans, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (human, _) =>
        {
            var tag = redaction[human];
            try
            {
                foreach (var type in Types)
                {
                    var v1 = await GetV1Async(http, address, secret, $"/api/tracking/{type}/{Uri.EscapeDataString(human)}");
                    var v1Rows = ListOf(v1, type);
                    var v2Rows = (await proxy.GetByUserAsync(type, human)).EnumerateArray().ToList();
                    CompareRows(tally, $"type:{type}", type, tag, v1Rows, v2Rows, activeRead: true);
                }

                var all1 = await GetV1Async(http, address, secret, $"/api/tracking/all/{Uri.EscapeDataString(human)}");
                var all2 = await proxy.GetAllTrackingAsync(human);
                CompareSnapshot(tally, "all", tag, all1, all2, activeRead: true);

                var ap1 = await GetV1Async(http, address, secret, $"/api/tracking/allProfiles/{Uri.EscapeDataString(human)}?includeDescriptions=true");
                var ap2 = await proxy.GetAllTrackingAllProfilesAsync(human);
                CompareSnapshot(tally, "allProfiles", tag, ap1, ap2, activeRead: false);
                CompareProfiles(tally, tag, ap1, ap2);

                tally.Humans.Increment("done");
            }
            catch (Exception ex)
            {
                tally.Errors.Add($"{tag}: {ex.GetType().Name}: {Redact(ex.Message, human, tag)}");
            }
        });

        var report = tally.Render(humans.Count);
        await File.WriteAllTextAsync(reportPath, report);

        Assert.True(tally.Findings.IsEmpty && tally.Errors.IsEmpty, $"Non-whitelisted differences; see {reportPath}\n{report[..Math.Min(report.Length, 4000)]}");
    }

    private static string Redact(string message, string human, string tag) => message.Replace(human, tag, StringComparison.Ordinal);

    private static PoracleTrackingProxy CreateProxy(HttpClient http, string address, string secret)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Poracle:ApiAddress"] = address,
                ["Poracle:ApiSecret"] = secret,
            })
            .Build();

        var profile = new Mock<IPoracleServerProfileService>();
        profile
            .Setup(p => p.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PoracleServerProfile { Version = "5.4.0", Reachable = true, CheckedAt = DateTimeOffset.UtcNow });

        return new PoracleTrackingProxy(
            http,
            config,
            profile.Object,
            Mock.Of<IPoracleV2SchemaService>(),
            Mock.Of<IInvasionGruntNameService>(),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<PoracleTrackingProxy>.Instance);
    }

    private static async Task<JsonElement> GetV1Async(HttpClient http, string address, string secret, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address + path);
        request.Headers.Add("X-Poracle-Secret", secret);
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static List<JsonElement> ListOf(JsonElement root, string key) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray()]
            : [];

    private static void CompareSnapshot(Tally tally, string surface, string tag, JsonElement v1, JsonElement v2, bool activeRead)
    {
        foreach (var type in Types)
        {
            if (v1.TryGetProperty(type, out var v1List) && v1List.ValueKind == JsonValueKind.Null)
            {
                tally.Shape.Increment($"{surface}: v1 '{type}' null, v2 []");
            }

            CompareRows(tally, $"{surface}:{type}", type, tag, ListOf(v1, type), ListOf(v2, type), activeRead);
        }

        foreach (var key in v1.EnumerateObject().Select(p => p.Name).Except(Types).Except(["status", "profile"]))
        {
            if (!v2.TryGetProperty(key, out _))
            {
                tally.Shape.Increment($"{surface}: key '{key}' only in v1");
            }
        }

        foreach (var key in v2.EnumerateObject().Select(p => p.Name).Except(Types).Except(["status", "profile"]))
        {
            if (!v1.TryGetProperty(key, out _))
            {
                tally.Shape.Increment($"{surface}: key '{key}' only in v2");
            }
        }
    }

    private static void CompareProfiles(Tally tally, string tag, JsonElement v1, JsonElement v2)
    {
        var p1 = ListOf(v1, "profile").ToDictionary(p => p.GetProperty("profile_no").GetInt32());
        var p2 = ListOf(v2, "profile").ToDictionary(p => p.GetProperty("profile_no").GetInt32());
        tally.Counts.Increment("allProfiles:profile rows v1", p1.Count);
        tally.Counts.Increment("allProfiles:profile rows v2", p2.Count);

        foreach (var (no, row) in p1)
        {
            if (!p2.TryGetValue(no, out var other))
            {
                tally.Findings.Add($"allProfiles:profile {tag} profile_no={no}: missing from v2");
                continue;
            }

            foreach (var diff in Diff(row, other))
            {
                tally.Findings.Add($"allProfiles:profile {tag} profile_no={no}: {diff.Key} v1={Redact(diff.V1, row.GetProperty("id").ToString(), tag)} v2={Redact(diff.V2, row.GetProperty("id").ToString(), tag)}");
            }
        }

        foreach (var no in p2.Keys.Except(p1.Keys))
        {
            tally.Findings.Add($"allProfiles:profile {tag} profile_no={no}: only in v2");
        }
    }

    private static void CompareRows(Tally tally, string surface, string type, string tag, List<JsonElement> v1Rows, List<JsonElement> v2Rows, bool activeRead)
    {
        tally.Counts.Increment($"{surface} rules v1", v1Rows.Count);
        tally.Counts.Increment($"{surface} rules v2", v2Rows.Count);

        var byUid2 = new Dictionary<int, JsonElement>();
        foreach (var row in v2Rows)
        {
            var uid = row.GetProperty("uid").GetInt32();
            if (!byUid2.TryAdd(uid, row))
            {
                tally.Findings.Add($"{surface} {tag} uid={uid}: duplicate uid in v2 output");
            }
        }

        var seen = new HashSet<int>();
        foreach (var row in v1Rows)
        {
            var uid = row.GetProperty("uid").GetInt32();
            seen.Add(uid);

            if (!byUid2.TryGetValue(uid, out var other))
            {
                tally.Findings.Add($"{surface} {tag} uid={uid}: in v1, missing from v2");
                continue;
            }

            var unexplained = 0;
            foreach (var diff in Diff(row, other))
            {
                var reason = Whitelisted(type, diff.Key, row, other, activeRead);
                if (reason is null)
                {
                    unexplained++;
                    tally.Findings.Add($"{surface} {tag} uid={uid}: {diff.Key} v1={diff.V1} v2={diff.V2}");
                }
                else
                {
                    tally.Whitelisted.Increment($"{surface}: {reason}");
                }
            }

            tally.Counts.Increment(unexplained == 0 ? $"{surface} rules equal" : $"{surface} rules differing");
        }

        foreach (var uid in byUid2.Keys.Except(seen))
        {
            tally.Findings.Add($"{surface} {tag} uid={uid}: only in v2");
        }
    }

    private static string? Whitelisted(string type, string key, JsonElement v1, JsonElement v2, bool activeRead)
    {
        var a = v1.TryGetProperty(key, out var x) ? x : default;
        var b = v2.TryGetProperty(key, out var y) ? y : default;

        bool Is(JsonElement e, int n) => e.ValueKind == JsonValueKind.Number && e.GetDecimal() == n;
        string? Str(JsonElement row, string k) => row.TryGetProperty(k, out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;

        switch (key)
        {
            case "pvp_ranking_best" when Is(a, 0) && Is(b, 1):
                return "legacy zero pvp_ranking_best 0->1";
            case "pvp_ranking_worst" when Is(a, 0) && Is(b, 4096):
                return "legacy zero pvp_ranking_worst 0->4096";
            case "size" when Is(a, 0) && Is(b, -1):
                return "legacy zero size 0->-1";
            case "gym_id" or "station_id"
                when (a.ValueKind == JsonValueKind.String && a.GetString() == "" && (b.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined))
                  || (b.ValueKind == JsonValueKind.String && b.GetString() == "" && (a.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)):
                return $"{key} \"\" == null";
            case "profile_no" when activeRead && b.ValueKind == JsonValueKind.Undefined:
                return "profile_no absent on active read";
            case "gender" when type == "invasion" && Is(b, 0) && PokestopEventTypes.IsEventName(Str(v1, "grunt_type")):
                return "event row gender -> 0";
            case "gender" when type == "invasion" && Is(b, 0) && BossGrunts.Contains(Str(v1, "grunt_type") ?? ""):
                return $"invasion gender dropped on '{Str(v1, "grunt_type")}'";
            case "clean" when EditBitDroppedTypes.Contains(type)
                && a.ValueKind == JsonValueKind.Number && b.ValueKind == JsonValueKind.Number
                && (a.GetInt32() & CleanFlags.Edit) != 0 && (a.GetInt32() & ~CleanFlags.Edit) == b.GetInt32():
                return "edit bit dropped";
            case "level" when type is "raid" or "maxbattle" && Is(a, 9000) && Is(b, 90)
                && v1.TryGetProperty("pokemon_id", out var pid) && Is(pid, 9000):
                return "level 9000 + pokemon 9000 -> 90";
            default:
                return null;
        }
    }

    private static IEnumerable<(string Key, string V1, string V2)> Diff(JsonElement v1, JsonElement v2)
    {
        var keys = v1.EnumerateObject().Select(p => p.Name).Union(v2.EnumerateObject().Select(p => p.Name));
        foreach (var key in keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var hasA = v1.TryGetProperty(key, out var a);
            var hasB = v2.TryGetProperty(key, out var b);
            if (hasA && hasB && DeepEquals(a, b))
            {
                continue;
            }

            yield return (key, hasA ? a.GetRawText() : "<absent>", hasB ? b.GetRawText() : "<absent>");
        }
    }

    private static bool DeepEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
        {
            return false;
        }

        return a.ValueKind switch
        {
            JsonValueKind.Number => a.GetDecimal() == b.GetDecimal(),
            JsonValueKind.String => a.GetString() == b.GetString(),
            JsonValueKind.Array => a.GetArrayLength() == b.GetArrayLength()
                && a.EnumerateArray().Zip(b.EnumerateArray()).All(p => DeepEquals(p.First, p.Second)),
            JsonValueKind.Object => a.EnumerateObject().Count() == b.EnumerateObject().Count()
                && a.EnumerateObject().All(p => b.TryGetProperty(p.Name, out var o) && DeepEquals(p.Value, o)),
            _ => true,
        };
    }

    private sealed class Counter
    {
        private readonly ConcurrentDictionary<string, long> _values = new(StringComparer.Ordinal);

        public void Increment(string key, long by = 1) => this._values.AddOrUpdate(key, by, (_, v) => v + by);

        public IEnumerable<KeyValuePair<string, long>> Sorted => this._values.OrderBy(kv => kv.Key, StringComparer.Ordinal);
    }

    private sealed class Tally
    {
        public Counter Humans { get; } = new();

        public Counter Counts { get; } = new();

        public Counter Whitelisted { get; } = new();

        public Counter Shape { get; } = new();

        public ConcurrentBag<string> Findings { get; } = [];

        public ConcurrentBag<string> Errors { get; } = [];

        public string Render(int humans)
        {
            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $"humans: {humans}; completed: {this.Humans.Sorted.FirstOrDefault().Value}; errors: {this.Errors.Count}");
            sb.AppendLine().AppendLine("== counts ==");
            foreach (var (k, v) in this.Counts.Sorted)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"{k}: {v}");
            }

            sb.AppendLine().AppendLine("== whitelisted differences (field occurrences) ==");
            foreach (var (k, v) in this.Whitelisted.Sorted)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"{k}: {v}");
            }

            sb.AppendLine().AppendLine("== shape notes (human count) ==");
            foreach (var (k, v) in this.Shape.Sorted)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"{k}: {v}");
            }

            sb.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"== non-whitelisted differences ({this.Findings.Count}) ==");
            foreach (var f in this.Findings.OrderBy(f => f, StringComparer.Ordinal))
            {
                sb.AppendLine(f);
            }

            sb.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"== errors ({this.Errors.Count}) ==");
            foreach (var e in this.Errors.OrderBy(e => e, StringComparer.Ordinal))
            {
                sb.AppendLine(e);
            }

            return sb.ToString();
        }
    }
}

/// <summary>A fact that runs only when <c>PWEB_BED_PORACLE</c> points at a test bed.</summary>
public sealed class BedFactAttribute : FactAttribute
{
    public BedFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PWEB_BED_PORACLE")))
        {
            this.Skip = "Bed-only: set PWEB_BED_PORACLE, PWEB_BED_SECRET and PWEB_BED_HUMANS to run against a PoracleNG test bed.";
        }
    }
}
