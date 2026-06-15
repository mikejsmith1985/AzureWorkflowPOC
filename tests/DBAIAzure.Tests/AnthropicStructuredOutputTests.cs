using DBAIAzure.Connectors;
using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Unit tests for the structured tool-use parsing path. They exercise the pure
/// <see cref="AnthropicChatCompletionService.ParseToolUseResult{T}"/> helper against hardcoded
/// Anthropic response bodies — no HTTP, no API key — proving the tool_use input binds to a record.
/// </summary>
public class AnthropicStructuredOutputTests
{
    private const string ToolName = "report_phase_validation";

    [Fact]
    public void ParseToolUseResult_BindsToolInput_ToTypedRecord()
    {
        // A realistic Anthropic forced-tool-use response: a single tool_use block whose input
        // matches the PhaseValidationResult schema.
        var responseBody = """
        {
          "id": "msg_01",
          "type": "message",
          "role": "assistant",
          "content": [
            {
              "type": "tool_use",
              "id": "toolu_01",
              "name": "report_phase_validation",
              "input": {
                "summary": "The spec defines the phase handler end to end.",
                "gaps": [
                  { "label": "No SLA", "description": "Response time target is unstated." }
                ]
              }
            }
          ]
        }
        """;

        var result = AnthropicChatCompletionService.ParseToolUseResult<PhaseValidationResult>(responseBody, ToolName);

        Assert.Equal("The spec defines the phase handler end to end.", result.Summary);
        Assert.Single(result.Gaps);
        Assert.Equal("No SLA", result.Gaps[0].Label);
        Assert.Equal("Response time target is unstated.", result.Gaps[0].Description);
    }

    [Fact]
    public void ParseToolUseResult_BindsEmptyGaps_WhenNoneReported()
    {
        var responseBody = """
        {
          "content": [
            {
              "type": "tool_use",
              "name": "report_phase_validation",
              "input": { "summary": "All good.", "gaps": [] }
            }
          ]
        }
        """;

        var result = AnthropicChatCompletionService.ParseToolUseResult<PhaseValidationResult>(responseBody, ToolName);

        Assert.Equal("All good.", result.Summary);
        Assert.Empty(result.Gaps);
    }

    [Fact]
    public void ParseToolUseResult_Throws_WhenNoMatchingToolUseBlock()
    {
        // A plain text response with no forced tool_use block should be rejected, not silently parsed.
        var responseBody = """
        { "content": [ { "type": "text", "text": "I cannot do that." } ] }
        """;

        Assert.Throws<InvalidOperationException>(() =>
            AnthropicChatCompletionService.ParseToolUseResult<PhaseValidationResult>(responseBody, ToolName));
    }
}
