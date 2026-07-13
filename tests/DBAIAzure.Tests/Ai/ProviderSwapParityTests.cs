// BYO-AI swap parity (spec-019 T041 / SC-008): switching the active provider runs an identical pipeline
// flow with zero change to any pipeline or executor — the orchestration depends only on IChatClient.
using DBAIAzure.Connectors.Ai;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.Ai;
using DBAIAzure.Processes.Pipeline.Maf;
using DBAIAzure.Tests.Parity;
using Microsoft.Extensions.AI;
using Xunit;

namespace DBAIAzure.Tests.Ai;

/// <summary>
/// Runs the intake workflow with the <see cref="IChatClient"/> from two different registered providers and
/// asserts the executor sequence is identical — proving a provider swap changes nothing in the pipeline
/// (the same <see cref="MafIntakeWorkflowFactory.Build"/> graph, no step/executor code touched).
/// </summary>
public sealed class ProviderSwapParityTests
{
    // A provider that hands back a pre-scripted client — a new provider is a registration, not a code change.
    private sealed class StubProvider(string id, IChatClient client) : IChatClientProvider
    {
        public string ProviderId { get; } = id;
        public IChatClient Create(AiProviderConfig config) => client;
    }

    private static RecordedChatClient ReadyTicketScript() => new(new[]
    {
        RecordedTurn.With("{\"title\":\"Sample\",\"description\":\"Sample description.\"}", 40, 12),
        RecordedTurn.With("{\"is_ready\":true,\"missing_fields\":[],\"reasoning\":\"clear\"}", 30, 8),
        RecordedTurn.With("{\"points\":5,\"reasoning\":\"comparable to the CRUD anchor\"}", 25, 8),
    }, repeatLast: true);

    private static TicketState SampleTicket => new() { TicketId = "INC0001", Title = "Sample", Description = "Sample description." };

    [Fact]
    public async Task IntakeFlow_IsIdentical_WhenTheActiveProviderIsSwapped()
    {
        // Two distinct providers, each yielding an identically-scripted client; selection is by config id.
        var registry = new ChatClientProviderRegistry(new IChatClientProvider[]
        {
            new StubProvider("provider-a", ReadyTicketScript()),
            new StubProvider("provider-b", ReadyTicketScript()),
        });

        var sequenceA = await RunIntakeAsync(registry.CreateActive(new AiProviderConfig("provider-a", "model", "key")));
        var sequenceB = await RunIntakeAsync(registry.CreateActive(new AiProviderConfig("provider-b", "model", "key")));

        Assert.Equal(
            new[] { MafExecutorIds.Intake, MafExecutorIds.Validation, MafExecutorIds.Estimation, MafExecutorIds.Action },
            sequenceA);
        Assert.Equal(sequenceA, sequenceB); // swapping the provider changed nothing in the pipeline
    }

    private static async Task<IReadOnlyList<string>> RunIntakeAsync(IChatClient chatClient)
    {
        var workflow = MafIntakeWorkflowFactory.Build(chatClient);
        var observation = await MafWorkflowRunner.RunAsync(workflow, SampleTicket);
        return observation.ExecutorSequence;
    }
}
