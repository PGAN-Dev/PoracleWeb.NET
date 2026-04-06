using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pgan.PoracleWebNet.Core.Models;

public class FortChange
{
    public int Uid
    {
        get; set;
    }
    public string Id { get; set; } = string.Empty;
    public string? Ping
    {
        get; set;
    }
    public int Distance
    {
        get; set;
    }
    /// <summary>
    /// Fort type to track. Valid values: <c>pokestop</c>, <c>gym</c>, <c>everything</c>.
    /// See <see cref="FortChangeOptions.ValidFortTypes"/>.
    /// </summary>
    public string? FortType
    {
        get; set;
    }
    public int IncludeEmpty
    {
        get; set;
    }

    /// <summary>
    /// Change types to monitor. Valid values: <c>name</c>, <c>location</c>, <c>image_url</c>, <c>removal</c>, <c>new</c>.
    /// An empty list means "all changes". See <see cref="FortChangeOptions.ValidChangeTypes"/>.
    /// PoracleNG may return this as a JSON string or a native array — the converter handles both.
    /// </summary>
    [JsonConverter(typeof(StringOrArrayConverter))]
    public List<string> ChangeTypes { get; set; } = [];
    public int Clean
    {
        get; set;
    }
    public string? Template
    {
        get; set;
    }
    public int ProfileNo
    {
        get; set;
    }
}

/// <summary>
/// Handles PoracleNG returning change_types as either a native JSON array or a JSON-encoded string.
/// e.g. both <c>["name","location"]</c> and <c>"[\"name\",\"location\"]"</c> deserialize to List&lt;string&gt;.
/// </summary>
public class StringOrArrayConverter : JsonConverter<List<string>>
{
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var list = new List<string>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType == JsonTokenType.String)
                {
                    list.Add(reader.GetString()!);
                }
            }

            return list;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (string.IsNullOrEmpty(str))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<string>>(str) ?? [];
        }

        if (reader.TokenType == JsonTokenType.Null)
        {
            return [];
        }

        return [];
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}
