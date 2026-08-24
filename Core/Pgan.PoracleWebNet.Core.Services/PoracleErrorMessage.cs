using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Pgan.PoracleWebNet.Core.Services;

/// <summary>
/// Reads whatever explanation Poracle returned with a refusal, across both of its wire formats.
/// </summary>
/// <remarks>
/// <para>
/// PoracleNG 5.2.1 replaced the <c>{status, message}</c> error body with RFC 9457
/// <c>application/problem+json</c> -- <c>{type, title, status, detail, errors[]}</c> -- and moved a
/// number of 400s to 422. Both shapes are read here by field name rather than by branching on a
/// version or a content type: the names do not collide, so one reader serves both, and a reverse
/// proxy that rewrites <c>Content-Type</c> cannot cost a user their error message.
/// </para>
/// <para>
/// <c>status</c> is read last and only when it is a string. The old shape used it for a word; the new
/// one uses it for the HTTP code, and answering a user with "422" explains nothing. <c>title</c> is
/// last of the problem+json fields for the same reason -- it is the status phrase
/// ("Unprocessable Entity"), not a description of what was wrong.
/// </para>
/// </remarks>
public static class PoracleErrorMessage
{
    /// <summary>Longest body echoed verbatim when nothing could be parsed out of it.</summary>
    private const int MaxRawBodyLength = 300;

    /// <summary>Most specific first. See the remarks on <see cref="PoracleErrorMessage"/> for the ordering.</summary>
    private static readonly string[] MessageProperties = ["detail", "message", "error", "status", "title"];

    /// <summary>Field-level entries quoted before the list is summarised, to keep the message readable.</summary>
    private const int MaxFieldErrors = 3;

    /// <summary>
    /// Returns Poracle's own explanation for a failed response, or <paramref name="fallback"/> when it
    /// did not give one.
    /// </summary>
    public static async Task<string> ExtractAsync(HttpResponseMessage response, string fallback)
    {
        var body = await response.Content.ReadAsStringAsync();

        try
        {
            var root = JsonDocument.Parse(body).RootElement;

            // A body that parsed is Poracle talking to us in a shape we know. If we cannot find an
            // explanation in it we fall back rather than echoing it: raw JSON in a snackbar tells a user
            // less than a plain sentence does. The echo below is for bodies that are not JSON at all,
            // such as a reverse proxy's "upstream connect error".
            if (root.ValueKind == JsonValueKind.String)
            {
                var only = root.GetString();

                return string.IsNullOrWhiteSpace(only) ? fallback : only;
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                // problem+json errors[] names the individual fields that were refused, which is more
                // use than the summary in detail, so it wins when present.
                var fieldErrors = ReadFieldErrors(root);

                if (fieldErrors is not null)
                {
                    return fieldErrors;
                }

                foreach (var name in MessageProperties)
                {
                    if (root.TryGetProperty(name, out var value)
                        && value.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(value.GetString()))
                    {
                        return value.GetString()!;
                    }
                }

                return fallback;
            }
        }
        catch (JsonException)
        {
            // Not JSON; the raw body is still better than nothing, as long as it is short.
        }

        return string.IsNullOrWhiteSpace(body) || body.Length > MaxRawBodyLength ? fallback : body;
    }

    /// <summary>
    /// Renders <c>errors[]</c> as "field: what was wrong", or null when the response carries no usable
    /// field-level detail.
    /// </summary>
    /// <remarks>
    /// <c>location</c> arrives as a path into the submitted body (<c>body.min_iv</c>); only the last
    /// segment means anything to someone looking at a form, so the rest is dropped.
    /// </remarks>
    private static string? ReadFieldErrors(JsonElement root)
    {
        if (!root.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var rendered = new List<string>();

        foreach (var entry in errors.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var message = ReadNonEmptyString(entry, "message");

            if (message is null)
            {
                continue;
            }

            var field = LastSegment(ReadNonEmptyString(entry, "location"));
            rendered.Add(field is null ? message : $"{field}: {message}");
        }

        if (rendered.Count == 0)
        {
            return null;
        }

        var shown = new StringBuilder(string.Join("; ", rendered.Take(MaxFieldErrors)));

        if (rendered.Count > MaxFieldErrors)
        {
            shown.Append(CultureInfo.InvariantCulture, $" (and {rendered.Count - MaxFieldErrors} more)");
        }

        return shown.ToString();
    }

    /// <summary>Returns the segment after the last dot, or null when there is nothing to return.</summary>
    private static string? LastSegment(string? location)
    {
        if (location is null)
        {
            return null;
        }

        var lastDot = location.LastIndexOf('.');
        var segment = lastDot >= 0 ? location[(lastDot + 1)..] : location;

        return string.IsNullOrWhiteSpace(segment) ? null : segment;
    }

    private static string? ReadNonEmptyString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()
                : null;
}
