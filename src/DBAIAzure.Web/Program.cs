using DBAIAzure.Connectors;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

// Suppress SKEXP0080 solution-wide: SK Process Framework is experimental in 1.77.0
#pragma warning disable SKEXP0080

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ─────────────────────────────────────────────────────────────
var anthropicKey = builder.Configuration["Anthropic:ApiKey"] ?? string.Empty;
var anthropicModel = builder.Configuration["Anthropic:Model"] ?? "claude-sonnet-4-6";

if (string.IsNullOrWhiteSpace(anthropicKey) || anthropicKey.StartsWith("REPLACE"))
    throw new InvalidOperationException(
        "Anthropic:ApiKey is required. Add it to appsettings.Development.json (gitignored).");

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// PipelineOrchestrator uses a kernel factory so each run gets its own kernel with a
// fresh BoundProgressReporter registered — decouples the orchestrator from SK wiring.
builder.Services.AddSingleton<PipelineOrchestrator>(_ =>
{
    Func<IProgressReporter, Kernel> kernelFactory = reporter =>
    {
        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.Services.AddSingleton<IChatCompletionService>(
            new AnthropicChatCompletionService(anthropicKey, anthropicModel));
        kernelBuilder.Services.AddSingleton<IProgressReporter>(reporter);
        return kernelBuilder.Build();
    };

    return new PipelineOrchestrator(kernelFactory);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.UseStaticFiles();
app.UseRouting();

app.MapRazorPages();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
