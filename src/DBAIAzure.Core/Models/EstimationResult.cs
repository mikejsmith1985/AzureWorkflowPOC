using System.Text.Json.Serialization;

namespace DBAIAzure.Core.Models;

public record EstimationResult
{
    [JsonPropertyName("points")]
    public required int Points { get; init; }

    [JsonPropertyName("reasoning")]
    public required string Reasoning { get; init; }
}
