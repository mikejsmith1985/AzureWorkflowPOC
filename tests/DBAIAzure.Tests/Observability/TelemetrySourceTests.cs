// US5 observability (spec-019 T048 / SC-006): model-call activities are emitted under the registered
// MAF/M.E.AI OpenTelemetry source, so a run's spans reach Azure Monitor under the new source with no gap.
using System.Diagnostics;
using DBAIAzure.Core.Diagnostics;
using DBAIAzure.Tests.Parity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.Observability;

/// <summary>
/// Verifies that wrapping the model client with <see cref="OpenTelemetryChatClient"/> under
/// <see cref="AiTelemetrySourceNames.ChatClient"/> produces an OpenTelemetry <see cref="Activity"/> from
/// that source on each model call — the source an exporter (Azure Monitor) is configured to collect.
/// </summary>
public sealed class TelemetrySourceTests
{
    [Fact]
    public async Task ModelCall_EmitsActivity_UnderTheRegisteredMafSource()
    {
        var captured = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AiTelemetrySourceNames.ChatClient,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => captured.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);

        IChatClient inner = new RecordedChatClient(RecordedTurn.With("ok", 10, 5, modelId: "claude-opus-4-8"));
        using var client = new OpenTelemetryChatClient(inner, NullLogger.Instance, AiTelemetrySourceNames.ChatClient);

        await client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "hi") });

        var activity = Assert.Single(captured);
        Assert.Equal(AiTelemetrySourceNames.ChatClient, activity.Source.Name);
    }
}
