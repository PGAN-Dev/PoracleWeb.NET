using System.Text.Json;

namespace Pgan.PoracleWebNet.Core.Abstractions.Services;

/// <summary>
/// Builds a mock webhook payload for one alarm type that PoracleNG's <c>/api/test</c>
/// endpoint can enrich and render. Implementations are filter-aware: the payload they
/// produce honors the alarm's own filter fields so the DM preview matches what the alarm
/// would actually match on a real spawn.
/// </summary>
public interface ITestPayloadBuilder
{
    /// <summary>
    /// Alarm types this builder claims (e.g. <c>"pokemon"</c>, or <c>"raid"</c>+<c>"egg"</c>).
    /// The service dispatcher calls the first builder whose <see cref="CanBuild"/> returns true.
    /// </summary>
    public bool CanBuild(string alarmType);

    /// <summary>
    /// Build the wire <c>type</c> (as PoracleNG's test endpoint expects) and the raw webhook body.
    /// </summary>
    public Task<TestPayloadBuildResult> BuildAsync(TestPayloadContext context);
}

/// <summary>
/// Inputs shared across all builders.
/// </summary>
/// <param name="AlarmType">The UI alarm type (<c>pokemon</c>, <c>raid</c>, <c>egg</c>, <c>quest</c>, <c>invasion</c>, <c>lure</c>, <c>nest</c>, <c>gym</c>).</param>
/// <param name="Alarm">The raw alarm JSON returned from PoracleNG's tracking API — contains filter fields.</param>
/// <param name="Latitude">The user's location latitude (with sensible fallback).</param>
/// <param name="Longitude">The user's location longitude.</param>
/// <param name="Now">A single reference point for timestamp freshening inside one build.</param>
public readonly record struct TestPayloadContext(
    string AlarmType,
    JsonElement Alarm,
    double Latitude,
    double Longitude,
    DateTimeOffset Now);

/// <summary>
/// Wire <c>type</c> string (<c>pokemon</c>, <c>raid</c>, <c>pokestop</c>, <c>quest</c>, <c>gym</c>, <c>nest</c>)
/// and the webhook body as a key/value bag.
/// </summary>
public sealed record TestPayloadBuildResult(string WireType, Dictionary<string, object> Webhook);
