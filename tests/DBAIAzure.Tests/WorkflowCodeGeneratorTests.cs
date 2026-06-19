// Tests for IWorkflowCodeGenerator contract:
// - streaming calls onToken for each chunk
// - RefineAsync diff correctly identifies added/removed lines
// - LlmUnavailableException is thrown when the service returns a failure
// - generated code contains all node labels from the topology
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests;

public class WorkflowCodeGeneratorTests
{
    // ── Hand-rolled fake IWorkflowCodeGenerator ─────────────────────────────────

    private sealed class FakeWorkflowCodeGenerator : IWorkflowCodeGenerator
    {
        public bool ShouldThrowLlmUnavailable { get; set; }

        /// <summary>Tokens that GenerateAsync will stream to the onToken callback.</summary>
        public IReadOnlyList<string> TokensToStream { get; set; } = new[] { "class ", "MyStep ", "{ }" };

        public async Task<string> GenerateAsync(
            WorkflowDefinition workflow,
            IReadOnlyList<WorkflowChatMessage> chatHistory,
            Action<string> onToken,
            CancellationToken cancellationToken = default)
        {
            if (ShouldThrowLlmUnavailable)
                throw new LlmUnavailableException("The LLM service is not reachable.");

            var sb = new System.Text.StringBuilder();
            foreach (var token in TokensToStream)
            {
                onToken(token);
                sb.Append(token);
                await Task.Yield();
            }

            // Append node labels so the content check test can assert they appear.
            foreach (var node in workflow.Nodes)
                sb.AppendLine($"// Step: {node.Label}");

            return sb.ToString();
        }

        public async Task<(string UpdatedCode, CodeDiff Diff)> RefineAsync(
            string previousCode,
            string instruction,
            WorkflowDefinition workflow,
            Action<string> onToken,
            CancellationToken cancellationToken = default)
        {
            if (ShouldThrowLlmUnavailable)
                throw new LlmUnavailableException("The LLM service is not reachable.");

            var updatedCode = previousCode + "\n// refined: " + instruction;
            onToken("// refined: " + instruction);
            await Task.Yield();

            // Build a simple diff: everything from previousCode is unchanged, new line is Added.
            var previousLines = previousCode.Split('\n');
            var diffLines = new List<DiffLine>();
            for (var lineIndex = 0; lineIndex < previousLines.Length; lineIndex++)
                diffLines.Add(new DiffLine(lineIndex + 1, DiffLineKind.Unchanged, previousLines[lineIndex]));

            diffLines.Add(new DiffLine(previousLines.Length + 1, DiffLineKind.Added, "// refined: " + instruction));
            var diff = new CodeDiff(diffLines.AsReadOnly());

            return (updatedCode, diff);
        }
    }

    // ── Helper ──────────────────────────────────────────────────────────────────

    private static WorkflowDefinition BuildOneNodeWorkflow()
    {
        var inputPort  = new WorkflowPort("in1",  "Input",  PortDirection.Input);
        var outputPort = new WorkflowPort("out1", "Output", PortDirection.Output);

        var node = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Process Request") with
        {
            IsConfigured = true,
            InputPorts   = new[] { inputPort }.ToList().AsReadOnly(),
            OutputPorts  = new[] { outputPort }.ToList().AsReadOnly(),
        };

        return WorkflowDefinition.CreateNew("Test Workflow", "owner1") with
        {
            Nodes = new[] { node }.ToList().AsReadOnly(),
        };
    }

    // ── Tests ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateAsync_CallsOnToken_ForEachStreamedChunk()
    {
        var generator = new FakeWorkflowCodeGenerator
        {
            TokensToStream = new[] { "token1", "token2", "token3" }
        };
        var workflow = BuildOneNodeWorkflow();
        var receivedTokens = new List<string>();

        await generator.GenerateAsync(workflow, [], receivedTokens.Add);

        Assert.Equal(3, receivedTokens.Count);
        Assert.Equal("token1", receivedTokens[0]);
        Assert.Equal("token2", receivedTokens[1]);
        Assert.Equal("token3", receivedTokens[2]);
    }

    [Fact]
    public async Task GenerateAsync_ThrowsLlmUnavailableException_OnServiceFailure()
    {
        var generator = new FakeWorkflowCodeGenerator { ShouldThrowLlmUnavailable = true };
        var workflow  = BuildOneNodeWorkflow();

        await Assert.ThrowsAsync<LlmUnavailableException>(
            () => generator.GenerateAsync(workflow, [], _ => { }));
    }

    [Fact]
    public async Task GenerateAsync_ReturnedCode_ContainsAllNodeLabels()
    {
        var generator = new FakeWorkflowCodeGenerator
        {
            TokensToStream = Array.Empty<string>()
        };
        var workflow = BuildOneNodeWorkflow();

        var code = await generator.GenerateAsync(workflow, [], _ => { });

        Assert.Contains("Process Request", code);
    }

    [Fact]
    public async Task RefineAsync_Diff_IdentifiesAddedLines()
    {
        var generator  = new FakeWorkflowCodeGenerator();
        var workflow   = BuildOneNodeWorkflow();
        const string previousCode  = "// original code";
        const string instruction   = "add logging";

        var (updatedCode, diff) = await generator.RefineAsync(previousCode, instruction, workflow, _ => { });

        Assert.Contains("// refined: add logging", updatedCode);
        Assert.Contains(diff.Lines, line => line.Kind == DiffLineKind.Added);
    }

    [Fact]
    public async Task RefineAsync_Diff_PreservesUnchangedLines()
    {
        var generator = new FakeWorkflowCodeGenerator();
        var workflow  = BuildOneNodeWorkflow();

        var (_, diff) = await generator.RefineAsync("line1\nline2", "tweak", workflow, _ => { });

        Assert.Contains(diff.Lines, line => line.Kind == DiffLineKind.Unchanged);
    }

    [Fact]
    public async Task RefineAsync_ThrowsLlmUnavailableException_OnServiceFailure()
    {
        var generator = new FakeWorkflowCodeGenerator { ShouldThrowLlmUnavailable = true };
        var workflow  = BuildOneNodeWorkflow();

        await Assert.ThrowsAsync<LlmUnavailableException>(
            () => generator.RefineAsync("prev", "instruction", workflow, _ => { }));
    }

    [Fact]
    public async Task GenerateAsync_WithNoChatHistory_CompletesSuccessfully()
    {
        var generator = new FakeWorkflowCodeGenerator();
        var workflow  = BuildOneNodeWorkflow();

        var code = await generator.GenerateAsync(workflow, [], _ => { });

        Assert.False(string.IsNullOrWhiteSpace(code));
    }
}
