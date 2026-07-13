// Structured-output parity for the MAF model layer (spec-019 T036 / FR-011): the IChatClient-based
// structured completion binds schema-constrained JSON to the same typed records the SK forced-tool path did.
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Ai;
using DBAIAzure.Processes.Executors;
using DBAIAzure.Tests.Parity;
using Xunit;

namespace DBAIAzure.Tests.Ai;

/// <summary>
/// Verifies that <see cref="ChatClientStructuredCompletionService"/> deserialises schema-bound model output
/// to identical typed records — the routing decision and the phase-validation result the pipelines rely on —
/// so structured output survives the move off the SK forced-tool implementation.
/// </summary>
public sealed class StructuredOutputParityTests
{
    private static ChatClientStructuredCompletionService Service(string json) =>
        new(new RecordedChatClient(RecordedTurn.With(json, 20, 8)));

    [Fact]
    public async Task BindsRouteDecision_ToTypedRecord()
    {
        var service = Service("{\"SelectedPortLabel\":\"approve\"}");

        var decision = await service.GetStructuredAsync<RouteDecision>(
            systemPrompt: string.Empty,
            userMessage: "route this",
            toolName: "route",
            toolDescription: "choose a port",
            inputSchemaJson: RouteDecisionSchema.JsonSchema);

        Assert.Equal("approve", decision.SelectedPortLabel);
    }

    [Fact]
    public async Task BindsPhaseValidationResult_WithGaps()
    {
        var service = Service("{\"summary\":\"The spec is clear.\",\"gaps\":[{\"label\":\"Timeouts\",\"description\":\"unspecified\"}]}");

        var validation = await service.GetStructuredAsync<PhaseValidationResult>(
            systemPrompt: "you are a reviewer",
            userMessage: "validate this",
            toolName: PhaseValidationExecutor.ToolName,
            toolDescription: PhaseValidationExecutor.ToolDescription,
            inputSchemaJson: PhaseValidationExecutor.InputSchemaJson);

        Assert.Equal("The spec is clear.", validation.Summary);
        var gap = Assert.Single(validation.Gaps);
        Assert.Equal("Timeouts", gap.Label);
        Assert.Equal("unspecified", gap.Description);
    }
}
