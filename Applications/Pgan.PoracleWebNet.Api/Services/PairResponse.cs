using System.Text.Json;
using System.Text.Json.Nodes;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Api.Services;

/// <summary>
/// Adds <c>pairWarning: { side, reason }</c> to a response, and only when the other side of a linked pair did
/// not take the write. With no warning the body is returned as it was, so an unlinked person's response is
/// byte for byte today's.
/// </summary>
public static class PairResponse
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <param name="body">The response body, or null when the action answers no content.</param>
    /// <param name="warning">The writer's warning, if any.</param>
    /// <param name="arrayKey">Where a bare array body goes when a warning has to ride along with it.</param>
    public static object? With(object? body, PairWarning? warning, string arrayKey = "items")
    {
        if (warning is null)
        {
            return body;
        }

        var node = body is null ? new JsonObject() : JsonSerializer.SerializeToNode(body, Options);
        var result = node as JsonObject ?? new JsonObject { [arrayKey] = node };
        result["pairWarning"] = JsonSerializer.SerializeToNode(warning.ToBody(), Options);
        return result;
    }
}
