// End-to-end runtime tests: a realized node's accepted configuration actually reaches its Semantic
// Kernel step at execution time. Builds a real KernelProcess from a realized workflow and runs it
// through the local process runtime with a fake chat service — no network call (Article V / Article X).

using System.Runtime.CompilerServices;
using System.Text;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.NodeConfig;
using DBAIAzure.Processes;
using DBAIAzure.Processes.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Xunit;

namespace DBAIAzure.Tests;

public sealed class NodeRealizationRuntimeTests
{
    [Fact]
    public async Task Realized_agent_node_executes_its_accepted_instruction()
    {
        // A single agentic node, realized with a distinctive instruction.
        const string uniqueInstruction = "UNIQUE_REALIZED_INSTRUCTION_MARKER";
        var agentConfig = new AgentNodeConfig { Instruction = uniqueInstruction, ModelRef = "test-model" };
        var functionConfig = NodeConfigSerializer.ToFunctionConfig(WorkflowNodeType.AgenticReason, agentConfig);

        var agentNode = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Triage") with
        {
            GoalPrompt     = "PLAIN_LANGUAGE_GOAL_SHOULD_NOT_WIN",
            FunctionConfig = functionConfig,
            IsConfigured   = true,
            OutputPorts    = new[] { new WorkflowPort("out", "Output", PortDirection.Output) }.ToList().AsReadOnly(),
        };

        var workflow = new WorkflowDefinition
        {
            Id             = Guid.NewGuid(),
            Name           = "Realized",
            OwnerId        = "owner1",
            Nodes          = new[] { agentNode }.ToList().AsReadOnly(),
            Edges          = Array.Empty<WorkflowEdge>(),
            Settings       = new WorkflowSettings(),
            ChatHistory    = Array.Empty<WorkflowChatMessage>(),
            CreatedAt      = DateTimeOffset.UtcNow,
            LastModifiedAt = DateTimeOffset.UtcNow,
        };

        var recordingChat = new RecordingChatService();
        var kernel        = BuildKernel(recordingChat);
        var process       = new WorkflowRuntimeBuilder().Build(workflow);

        var startEvent = new KernelProcessEvent
        {
            Id   = WorkflowNodeEvents.NodeStart,
            Data = new WorkflowStepData { RunId = "test", NodeId = agentNode.Id, InputPayload = "Begin." },
        };

        await LocalKernelProcessFactory.RunToEndAsync(process, kernel, startEvent, TimeSpan.FromSeconds(10));

        // The realized instruction must have been used as the system message — not the goal fallback.
        Assert.Contains(uniqueInstruction, recordingChat.SystemMessages);
        Assert.DoesNotContain("PLAIN_LANGUAGE_GOAL_SHOULD_NOT_WIN", recordingChat.SystemMessages);
    }

    private static Kernel BuildKernel(IChatCompletionService chatService)
    {
        var builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton(chatService);
        return builder.Build();
    }

    /// <summary>Fake chat service that records every system message it is asked to complete.</summary>
    private sealed class RecordingChatService : IChatCompletionService
    {
        public List<string> SystemMessages { get; } = new();

        public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

        private void Capture(ChatHistory chatHistory)
        {
            foreach (var message in chatHistory.Where(message => message.Role == AuthorRole.System))
                SystemMessages.Add(message.Content ?? string.Empty);
        }

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null, CancellationToken cancellationToken = default)
        {
            Capture(chatHistory);
            return Task.FromResult<IReadOnlyList<ChatMessageContent>>(
                new[] { new ChatMessageContent(AuthorRole.Assistant, "ok") });
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Capture(chatHistory);
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, "ok");
            await Task.CompletedTask;
        }
    }
}
