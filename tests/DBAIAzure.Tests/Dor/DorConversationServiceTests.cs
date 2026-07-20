// Unit tests for the AI conversation service (spec-021 T035): the conversation prompt is interpolated with the
// outstanding gaps + human reply, the reply-evaluation schema is used, and the structured result is returned.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Core.Models.DorWorkflow.Config;
using DBAIAzure.Processes.Executors.Dor;
using Xunit;

namespace DBAIAzure.Tests.Dor;

public sealed class DorConversationServiceTests
{
    [Fact]
    public async Task EvaluateReply_InterpolatesGapsAndReply_AndReturnsEvaluation()
    {
        var resolved = new ReplyEvaluation(
            Resolved: true, RemainingGaps: Array.Empty<string>(),
            FieldUpdates: new Dictionary<string, string> { ["acceptance_criteria"] = "Given/When/Then" },
            ReplyMessage: "Thanks — that resolves it.");
        var completion = new FakeCompletion(resolved);
        var service = new DorConversationService(completion);

        var result = await service.EvaluateReplyAsync(
            new[] { "acceptance_criteria" }, "Here are the AC: ...", iteration: 1, new DorAiConfig());

        Assert.True(result.Resolved);
        Assert.Equal("Thanks — that resolves it.", result.ReplyMessage);
        Assert.Contains("acceptance_criteria", completion.CapturedSystemPrompt);   // {{failed_criteria}} filled
        Assert.Contains("Here are the AC", completion.CapturedSystemPrompt);        // {{human_response}} filled
        Assert.Equal(DorSchemas.ReplyEvaluationSchema, completion.CapturedSchema);
        Assert.Equal("dor_reply_eval", completion.CapturedToolName);
    }

    private sealed class FakeCompletion : IStructuredCompletionService
    {
        private readonly object _result;
        public string CapturedSystemPrompt { get; private set; } = "";
        public string CapturedSchema { get; private set; } = "";
        public string CapturedToolName { get; private set; } = "";
        public FakeCompletion(object result) => _result = result;

        public Task<T> GetStructuredAsync<T>(
            string systemPrompt, string userMessage, string toolName, string toolDescription,
            string inputSchemaJson, CancellationToken cancellationToken = default)
        {
            CapturedSystemPrompt = systemPrompt;
            CapturedSchema = inputSchemaJson;
            CapturedToolName = toolName;
            return Task.FromResult((T)_result);
        }
    }
}
