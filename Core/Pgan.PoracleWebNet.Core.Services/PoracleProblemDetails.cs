using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Turns whatever PoracleNG returned on a refusal into one sentence a person can act on.
/// </summary>
/// <remarks>
/// <para>
/// PoracleNG now speaks two error dialects and PoracleWeb has to read both. The frozen v1 surface answers
/// <c>{"message":"...","status":"error"}</c> at 400. <c>/api/v2</c> answers RFC 9457 problem+json at 422,
/// and in two shapes of its own: a schema failure carries <c>errors[]</c> with a <c>location</c>, while a
/// semantic refusal such as "override_areas and distance are mutually exclusive" carries only
/// <c>detail</c>. Confirmed live against 5.2.1.
/// </para>
/// <para>
/// The <c>location</c> is a JSON pointer into the request body, and its shape moves with the route:
/// <c>body.pokemon_id</c> on the single-object PUT, <c>body[0].pokemon_id</c> on the array-bodied POST.
/// Both prefixes are trimmed, because a user reading "body[0].pokemon_id: expected integer" learns less
/// than one reading "pokemon_id: expected integer".
/// </para>
/// <para>
/// NOTE: PR #811 (<c>fix/803-problem-json-errors</c>) introduces <c>PoracleErrorMessage.cs</c>, which reads
/// the same two shapes for the v1 create path. Whichever lands second should collapse the two into one —
/// they are the same job, and two of them will drift.
/// </para>
/// </remarks>
internal static class PoracleProblemDetails
{
    /// <summary>What to say when PoracleNG refused and explained nothing usable.</summary>
    public const string Unexplained = "Poracle rejected the alarm.";

    /// <summary>Field errors quoted before the rest are summarised, to keep the message readable.</summary>
    private const int MaxFieldErrors = 3;

    /// <summary>
    /// Reads an explanation out of a response body. Never throws: an unreadable body still has to produce
    /// something to show, and the alternative is a 500 for a request PoracleNG already described.
    /// </summary>
    public static string Describe(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Unexplained;
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(body);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // Not JSON. gin's plaintext "404 page not found" lands here, as does an HTML error page from
            // whatever proxy sits in front. Short bodies are still better than nothing.
            return body.Length > 300 ? Unexplained : body.Trim();
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return Unexplained;
        }

        var fieldErrors = FieldErrors(root);
        if (fieldErrors is not null)
        {
            return fieldErrors;
        }

        // v2 semantic refusal, then v1's own wording, then the problem title as a last resort.
        foreach (var name in new[] { "detail", "message", "error", "title" })
        {
            if (root.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return value.GetString()!;
            }
        }

        return Unexplained;
    }

    /// <summary>True when this body is PoracleNG's problem+json rather than the v1 shape.</summary>
    public static bool IsProblemJson(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.Number;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// "pokemon_id: expected integer", or null when this is not a schema failure.
    /// </summary>
    private static string? FieldErrors(JsonElement root)
    {
        if (!root.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var described = new StringBuilder();
        var shown = 0;
        var hidden = 0;

        foreach (var error in errors.EnumerateArray())
        {
            if (error.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var message = StringOf(error, "message");
            if (string.IsNullOrWhiteSpace(message))
            {
                continue;
            }

            // A body refused on a dozen fields would otherwise render a dozen clauses into a snackbar.
            // The first few name the problem; the count says there is more without spelling it out.
            if (shown == MaxFieldErrors)
            {
                hidden++;
                continue;
            }

            var field = TrimBodyPointer(StringOf(error, "location"));

            if (described.Length > 0)
            {
                described.Append("; ");
            }

            described.Append(string.IsNullOrWhiteSpace(field) ? message : $"{field}: {message}");
            shown++;
        }

        if (hidden > 0)
        {
            described.Append(CultureInfo.InvariantCulture, $" (and {hidden} more)");
        }

        return described.Length > 0 ? described.ToString() : null;
    }

    /// <summary>Strips the <c>body</c> / <c>body[n]</c> prefix off a huma location pointer.</summary>
    private static string? TrimBodyPointer(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        var dot = location.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0)
        {
            return location;
        }

        var head = location[..dot];
        return head == "body" || (head.StartsWith("body[", StringComparison.Ordinal) && head.EndsWith(']'))
            ? location[(dot + 1)..]
            : location;
    }

    private static string? StringOf(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
