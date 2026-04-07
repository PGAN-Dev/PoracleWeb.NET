using System.Text.Json.Serialization;

namespace Pgan.PoracleWebNet.Core.Models;

public class TestAlertRequest
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public TestAlertTarget Target { get; set; } = new();

    [JsonPropertyName("webhook")]
    public object Webhook { get; set; } = new();
}

public class TestAlertTarget
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "discord:user";

    [JsonPropertyName("language")]
    public string Language { get; set; } = "en";

    [JsonPropertyName("template")]
    public string Template { get; set; } = string.Empty;

    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }
}
