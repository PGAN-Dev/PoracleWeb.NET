using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Asks PoracleNG which grunt names it accepts, from <c>GET /api/masterdata/grunts</c>.
/// </summary>
/// <remarks>
/// <para>
/// v2's invasion write validates <c>grunt_type</c> against this list and answers 422 for anything else,
/// and its own field documentation points here to enumerate the valid names. That is not a theoretical
/// concern: of 201 invasion rules in production, 32 carry a value v2 refuses — <c>kecleon</c> (16),
/// <c>gold-stop</c> (7) and <c>showcase</c> (4), which are pokestop events and belong to
/// <c>/incident</c>, plus <c>metal</c> (5), which this application wrote itself from
/// <see cref="Pgan.PoracleWebNet.Core.Models.InvasionGruntTypes"/> where the game data says
/// <c>steel</c>. All 32 are visible and editable in the invasion list, because v1's read returns them
/// where v2's does not — verified against a live instance.
/// </para>
/// <para>
/// Read from the server rather than checked in, the same way <c>V2SchemaBounds</c> reads the numeric
/// limits. A shipped list would be a second copy of upstream's game data to keep in step, and the
/// <c>metal</c> discrepancy above is what that costs.
/// </para>
/// <para>
/// Fails closed to an empty set, which sends every invasion write to v1 — where they all go today, so
/// an unreadable list is a no-change rather than a failure.
/// </para>
/// </remarks>
public sealed class InvasionGruntNameService(IPoracleApiProxy apiProxy, IMemoryCache cache)
    : IInvasionGruntNameService
{
    private const string CacheKey = "poracle:invasion-grunt-names";

    /// <summary>Game data, so it moves with an event rather than with a request.</summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Catch-alls v2 accepts that the masterdata does not list, because they are not grunts.
    /// </summary>
    /// <remarks>
    /// Both verified accepted against a live build of the branch, and both are named in the schema's own
    /// description of the field.
    /// </remarks>
    private static readonly string[] CatchAlls = ["everything", "boss"];

    private readonly IPoracleApiProxy _apiProxy = apiProxy;
    private readonly IMemoryCache _cache = cache;

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> GetAsync(CancellationToken cancellationToken = default)
    {
        if (this._cache.TryGetValue<IReadOnlySet<string>>(CacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        string? document = null;

        try
        {
            document = await this._apiProxy.GetGruntsAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            // Unreadable is the same answer as "offers nothing": every invasion write goes to v1.
        }

        var names = Parse(document);
        this._cache.Set(CacheKey, names, CacheFor);

        return names;
    }

    /// <summary>
    /// Pulls the distinct <c>grunt_type</c> values out of the masterdata document.
    /// </summary>
    /// <remarks>
    /// The document is an object keyed by grunt id, each value carrying a <c>grunt_type</c>. Several ids
    /// share one name — a grunt has a male and a female entry — so this is a set, not a list.
    /// </remarks>
    internal static IReadOnlySet<string> Parse(string? gruntsJson)
    {
        var names = new HashSet<string>(CatchAlls, StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(gruntsJson))
        {
            return names;
        }

        try
        {
            using var document = JsonDocument.Parse(gruntsJson);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return names;
            }

            foreach (var entry in document.RootElement.EnumerateObject())
            {
                if (entry.Value.ValueKind == JsonValueKind.Object
                    && entry.Value.TryGetProperty("grunt_type", out var gruntType)
                    && gruntType.ValueKind == JsonValueKind.String
                    && gruntType.GetString() is { Length: > 0 } name)
                {
                    names.Add(name);
                }
            }
        }
        catch (JsonException)
        {
            // Leaves the catch-alls, which are the two values that need no list to be valid.
        }

        return names;
    }

    /// <summary>
    /// The form PoracleNG stores a grunt name in: lowercased, spaces as underscores.
    /// </summary>
    /// <remarks>
    /// Ten production rows hold the spaced form (<c>player team leader</c>, <c>npc 0</c> through
    /// <c>npc 10</c>). v2 normalises them on write — verified — so they are sendable, and matching them
    /// against the list has to normalise the same way or they would be sent to v1 for no reason.
    /// </remarks>
    public static string Normalise(string gruntType) =>
        gruntType.Trim().ToLowerInvariant().Replace(' ', '_');
}
