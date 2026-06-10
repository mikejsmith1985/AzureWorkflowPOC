using System.Text.Json.Serialization;

namespace DBAIAzure.Core.Models;

public record DorVerdict
{
    [JsonPropertyName("is_ready")]
    public required bool IsReady { get; init; }

    [JsonPropertyName("missing_fields")]
    public IReadOnlyList<string> MissingFields { get; init; } = [];

    [JsonPropertyName("reasoning")]
    public required string Reasoning { get; init; }
}
