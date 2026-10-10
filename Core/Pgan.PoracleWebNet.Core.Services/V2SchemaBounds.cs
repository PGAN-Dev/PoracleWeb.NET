using System.Text.Json;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Reads the numeric limits a PoracleNG instance declares for its own v2 tracking rules, out of the
/// <c>/openapi.json</c> it publishes.
/// </summary>
/// <remarks>
/// <para>
/// These are read from the server rather than checked in on purpose. PoracleNG bounds every numeric
/// filter field on a branch that has not been released, and PoracleWeb.NET stores values outside ten of
/// those bounds -- values PoracleNG itself wrote. Shipping the limits as a table would start refusing
/// those rows on servers that still accept them: measured against a live 5.2.1, that is 68% of the
/// Pokemon rules on one instance dropping off the v2 write path to guard against a server nobody is
/// running yet.
/// </para>
/// <para>
/// Asking the instance instead means the guard switches on exactly when that instance enforces it, and
/// there is no table to keep in step with upstream. A 5.2.1 publishes one bound (<c>egg.level</c>,
/// minimum 1); the unreleased branch publishes 55.
/// </para>
/// <para>
/// Unreachable or unparseable means no bounds, never all bounds. A server we cannot ask behaves exactly
/// as it does today: the value goes out, and v2 answers for itself. Failing the other way would refuse
/// every filter on the strength of a failed HTTP call.
/// </para>
/// </remarks>
internal static class V2SchemaBounds
{
    /// <summary>The v2 rule schemas are named <c>V2{Type}Rule</c>; the wrappers around them are not.</summary>
    private const string SchemaPrefix = "V2";
    private const string SchemaSuffix = "Rule";

    /// <summary>
    /// Pulls the per-type field limits out of an OpenAPI document. Returns an empty map for anything it
    /// cannot read, rather than throwing: see the remarks on the class.
    /// </summary>
    internal static IReadOnlyDictionary<string, IReadOnlyDictionary<string, TrackingV2Translator.Bound>> Parse(string? openApiJson)
    {
        var empty = new Dictionary<string, IReadOnlyDictionary<string, TrackingV2Translator.Bound>>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(openApiJson))
        {
            return empty;
        }

        try
        {
            using var document = JsonDocument.Parse(openApiJson);

            if (!document.RootElement.TryGetProperty("components", out var components)
                || !components.TryGetProperty("schemas", out var schemas)
                || schemas.ValueKind != JsonValueKind.Object)
            {
                return empty;
            }

            var byType = new Dictionary<string, IReadOnlyDictionary<string, TrackingV2Translator.Bound>>(StringComparer.Ordinal);

            foreach (var schema in schemas.EnumerateObject())
            {
                if (TypeNameOf(schema.Name) is not { } type
                    || !schema.Value.TryGetProperty("properties", out var properties)
                    || properties.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var fields = new Dictionary<string, TrackingV2Translator.Bound>(StringComparer.Ordinal);

                foreach (var field in properties.EnumerateObject())
                {
                    var min = ReadInt(field.Value, "minimum");
                    var max = ReadInt(field.Value, "maximum");

                    if (min is not null || max is not null)
                    {
                        fields[field.Name] = new TrackingV2Translator.Bound(min, max);
                    }
                }

                if (fields.Count > 0)
                {
                    byType[type] = fields;
                }
            }

            return byType;
        }
        catch (JsonException)
        {
            return empty;
        }
    }

    /// <summary>
    /// <c>V2PokemonRule</c> becomes <c>pokemon</c>. Anything else, including the
    /// <c>V2CreateOutputV2PokemonRuleBody</c> envelopes that wrap these same schemas, is not a rule.
    /// </summary>
    private static string? TypeNameOf(string schemaName)
    {
        if (!schemaName.StartsWith(SchemaPrefix, StringComparison.Ordinal)
            || !schemaName.EndsWith(SchemaSuffix, StringComparison.Ordinal))
        {
            return null;
        }

        var inner = schemaName[SchemaPrefix.Length..^SchemaSuffix.Length];

        return inner.Length == 0 ? null : inner.ToLowerInvariant();
    }

    private static int? ReadInt(JsonElement schema, string name) =>
        schema.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;
}
