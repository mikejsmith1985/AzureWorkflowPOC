using Azure.Monitor.OpenTelemetry.Exporter;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes;
using DBAIAzure.Runner;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Spectre.Console;

// ── Configuration ─────────────────────────────────────────────────────────────
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var anthropicKey = config["Anthropic:ApiKey"] ?? string.Empty;
var anthropicModel = config["Anthropic:Model"] ?? "claude-3-5-sonnet-20241022";
var azureMonitorCs = config["AzureMonitor:ConnectionString"] ?? string.Empty;

if (string.IsNullOrWhiteSpace(anthropicKey) || anthropicKey.StartsWith("REPLACE"))
    throw new InvalidOperationException(
        "Anthropic:ApiKey is required. Add it to appsettings.Development.json (gitignored).");

// ── OpenTelemetry → Azure Monitor ─────────────────────────────────────────────
// Every SK prompt (text, tokens, latency) auto-traces without manual spans.
// Visible in Azure AI Foundry → Tracing view.
TracerProvider? tracerProvider = null;
if (!string.IsNullOrWhiteSpace(azureMonitorCs) && !azureMonitorCs.StartsWith("REPLACE"))
{
    tracerProvider = Sdk.CreateTracerProviderBuilder()
        .AddSource("Microsoft.SemanticKernel*")
        .AddAzureMonitorTraceExporter(o => o.ConnectionString = azureMonitorCs)
        .Build();
    AnsiConsole.MarkupLine("[dim]Azure Monitor tracing enabled.[/]");
}
else
{
    AnsiConsole.MarkupLine("[dim]Azure Monitor not configured — set AzureMonitor:ConnectionString to enable.[/]");
}

// ── Semantic Kernel ────────────────────────────────────────────────────────────
// AnthropicChatCompletionService implements SK's IChatCompletionService.
// Interview note: to swap to Azure OpenAI, replace the AddSingleton line with:
//   builder.AddAzureOpenAIChatCompletion(deployment, endpoint, apiKey)
// The process steps, routing, and observability code are completely unchanged.
var kernelBuilder = Kernel.CreateBuilder();
kernelBuilder.Services.AddSingleton<IChatCompletionService>(
    new AnthropicChatCompletionService(anthropicKey, anthropicModel));
var kernel = kernelBuilder.Build();

// ── Demo tickets ──────────────────────────────────────────────────────────────
TicketState[] demoTickets =
[
    // Happy path — well-formed ticket, should pass DoR validation on first try
    new()
    {
        TicketId = "INC0001001",
        Title = "Add dark mode toggle to admin console",
        Description = """
            Users have requested a dark mode option in the admin console.
            It should persist the preference in localStorage and apply a CSS class
            to the root element. The toggle should appear in the top-right nav bar.
            Acceptance criteria: preference survives page reload; all existing pages
            render without contrast issues in dark mode.
            """,
    },
    // HITL path — vague ticket, should trigger clarifying questions
    new()
    {
        TicketId = "INC0001002",
        Title = "Fix the thing with the login",
        Description = "Sometimes login doesn't work. Please fix.",
    },
];

AnsiConsole.Write(new Rule("[bold blue]DBAIAzure — Ticket Intake Pipeline (SK Process Framework)[/]"));
AnsiConsole.WriteLine();

foreach (var ticket in demoTickets)
{
    await RunTicketAsync(kernel, ticket);
    AnsiConsole.WriteLine();
}

tracerProvider?.Dispose();

// ── Per-ticket runner ──────────────────────────────────────────────────────────
static async Task RunTicketAsync(Kernel kernel, TicketState ticket)
{
    AnsiConsole.Write(new Rule($"[yellow]{ticket.TicketId}[/] {Markup.Escape(ticket.Title)}"));

    var process = IntakePipelineBuilder.Build();

#pragma warning disable SKEXP0080
    var runningProcess = await process.StartAsync(kernel, new KernelProcessEvent
    {
        Id = Events.TicketReceived,
        Data = ticket,
    });
#pragma warning restore SKEXP0080

    AnsiConsole.MarkupLine("[green]✓ Pipeline complete.[/]");
}
