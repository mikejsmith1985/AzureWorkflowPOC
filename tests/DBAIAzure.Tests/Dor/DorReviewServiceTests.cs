// Unit tests for the AI DoR-review service (spec-021 T022): the prompt is interpolated with the DoR document and
// ticket fields, the review schema is used, and the structured verdict is returned. Fake completion — no network.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Core.Models.DorWorkflow.Config;
using DBAIAzure.Processes.Executors.Dor;
using Xunit;

namespace DBAIAzure.Tests.Dor;

public sealed class DorReviewServiceTests
{
    private static readonly DorReviewResult PassVerdict =
        new("PASS", Array.Empty<CriterionResult>(), Array.Empty<string>(), new Dictionary<string, string>());

    [Fact]
    public async Task Review_InterpolatesDocumentAndFields_AndReturnsVerdict()
    {
        var completion = new FakeCompletion(PassVerdict);
        var service = new DorReviewService(completion);
        var fields = new Dictionary<string, string?> { ["summary"] = "Add export" };

        var result = await service.ReviewAsync(fields, "DOR-DOC-TEXT", new DorAiConfig());

        Assert.True(result.IsPass);
        Assert.Contains("DOR-DOC-TEXT", completion.CapturedSystemPrompt);   // {{dor_document}} filled
        Assert.Contains("Add export", completion.CapturedSystemPrompt);      // {{ticket_fields}} filled (serialized)
        Assert.Equal(DorSchemas.ReviewSchema, completion.CapturedSchema);
        Assert.Equal("dor_review", completion.CapturedToolName);
    }

    [Fact]
    public async Task Review_UsesConfiguredTemplate_WhenProvided()
    {
        var completion = new FakeCompletion(PassVerdict);
        var service = new DorReviewService(completion);
        var ai = new DorAiConfig { ReviewPromptTemplate = "CUSTOM PROMPT >> {{dor_document}}" };

        await service.ReviewAsync(new Dictionary<string, string?>(), "DOC", ai);

        Assert.StartsWith("CUSTOM PROMPT >> DOC", completion.CapturedSystemPrompt);
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
