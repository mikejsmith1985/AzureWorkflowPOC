// IFunctionInvocationFilter that emits LlmCallCompleted events for AI steps, enabling
// the Run History detail page to display model name and token counts per step.

#pragma warning disable SKEXP0001

using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using Microsoft.SemanticKernel;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Captures the model name and token usage from every AI function invocation and
/// fans them out as <see cref="WorkflowEventType.LlmCallCompleted"/> events to all
/// registered <see cref="IWorkflowObserver"/> implementations.
/// The current run ID is threaded via <see cref="DBAIAzure.Core.Diagnostics.LlmRunContext"/> so
/// observers can correlate the event with the right execution record. (Token capture now happens at
/// the connector via ILlmUsageReporter; this filter only fires when a connector populates usage
/// metadata, and is otherwise dormant.)
/// Never throws — exceptions are logged and swallowed so the kernel pipeline continues.
/// </summary>
public sealed class WorkflowFunctionInvocationFilter : IFunctionInvocationFilter
{
    private readonly IEnumerable<IWorkflowObserver> _observers;
    private readonly ILogger<WorkflowFunctionInvocationFilter> _logger;

    public WorkflowFunctionInvocationFilter(
        IEnumerable<IWorkflowObserver> observers,
        ILogger<WorkflowFunctionInvocationFilter> logger)
    {
        _observers = observers;
        _logger    = logger;
    }

    public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        var startedAt = DateTimeOffset.UtcNow;
        await next(context);
        var durationMs = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;

        try
        {
            var metadata = context.Result?.Metadata;
            if (metadata is null) return;

            // Usage comes back as an object whose type varies by connector.
            // Read it reflectively to stay connector-agnostic.
            if (!metadata.TryGetValue("Usage", out var usageRaw) || usageRaw is null) return;

            var usageType  = usageRaw.GetType();
            var inputProp  = usageType.GetProperty("InputTokenCount")
                          ?? usageType.GetProperty("PromptTokens");
            var outputProp = usageType.GetProperty("OutputTokenCount")
                          ?? usageType.GetProperty("CompletionTokens");

            var inputTokens  = inputProp  is not null ? (int?)Convert.ToInt32(inputProp.GetValue(usageRaw))  : null;
            var outputTokens = outputProp is not null ? (int?)Convert.ToInt32(outputProp.GetValue(usageRaw)) : null;

            var modelName = metadata.TryGetValue("ModelId", out var model) ? model?.ToString() : null;

            var evt = new WorkflowExecutionEvent(
                EventId:         Guid.NewGuid(),
                RunId:           DBAIAzure.Core.Diagnostics.LlmRunContext.CurrentRunId.Value ?? "unknown",
                NodeId:          null,
                NodeLabel:       $"{context.Function.PluginName}.{context.Function.Name}",
                EventType:       WorkflowEventType.LlmCallCompleted,
                OccurredAt:      DateTimeOffset.UtcNow,
                DurationMs:      durationMs,
                Outcome:         "success",
                LlmModelName:    modelName,
                LlmInputTokens:  inputTokens,
                LlmOutputTokens: outputTokens
            );

            foreach (var observer in _observers)
            {
                await observer.RecordEventAsync(evt).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to emit LlmCallCompleted event — continuing.");
        }
    }
}
#pragma warning restore SKEXP0001
