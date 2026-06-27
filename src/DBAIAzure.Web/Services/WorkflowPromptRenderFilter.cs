// IPromptRenderFilter implementation — logs prompt hash for audit, never the prompt text (Article IX).

using Microsoft.SemanticKernel;
using System.Security.Cryptography;
using System.Text;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Registered on the SK kernel factory as a placeholder for future prompt-logging.
/// In V1 it logs only the SHA-256 hash of the rendered prompt so the prompt can be
/// correlated with execution events without storing sensitive content (Article IX — Vault Zero-Knowledge).
/// </summary>
#pragma warning disable SKEXP0001
public sealed class WorkflowPromptRenderFilter : IPromptRenderFilter
{
    private readonly ILogger<WorkflowPromptRenderFilter> _logger;

    public WorkflowPromptRenderFilter(ILogger<WorkflowPromptRenderFilter> logger)
        => _logger = logger;

    public Task OnPromptRenderAsync(PromptRenderContext context, Func<PromptRenderContext, Task> next)
    {
        // Execute the render pipeline to populate context.RenderedPrompt.
        var task = next(context);

        // Hash after rendering so the length is stable, but never log the text.
        if (!string.IsNullOrEmpty(context.RenderedPrompt))
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(context.RenderedPrompt));
            _logger.LogDebug("PromptRender: function={FunctionName} sha256={Hash}",
                context.Function?.Name,
                Convert.ToHexString(hash)[..12]);
        }

        return task;
    }
}
#pragma warning restore SKEXP0001
