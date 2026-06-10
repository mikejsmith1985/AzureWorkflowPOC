using Azure.Monitor.OpenTelemetry.Exporter;
using DBAIAzure.Connectors;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Spectre.Console;

// Suppress SKEXP0080 solution-wide: SK Process Framework is experimental in 1.77.0
#pragma warning disable SKEXP0080

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
// HITL loop: run the process, check if it paused for human input, collect
// Console.ReadLine(), then restart with HumanResponded. Loops up to 3 rounds,
// matching the cap in ValidationStep (ClarificationRound >= 3 → Blocked).
static async Task RunTicketAsync(Kernel kernel, TicketState ticket)
{
    const int MaxClarificationRounds = 3;

    AnsiConsole.Write(new Rule($"[yellow]{ticket.TicketId}[/] {Markup.Escape(ticket.Title)}"));

    var currentTicket = ticket;

    for (int clarificationRound = 0; clarificationRound <= MaxClarificationRounds; clarificationRound++)
    {
        var hitlChannel = new HitlExternalChannel();
        var process = IntakePipelineBuilder.Build();

        // Round 0: start from the beginning with TicketReceived.
        // Rounds 1+: skip intake and start at ValidationStep with the PO's answer.
        var initialEvent = clarificationRound == 0
            ? new KernelProcessEvent { Id = Events.TicketReceived, Data = currentTicket }
            : new KernelProcessEvent { Id = Events.HumanResponded, Data = currentTicket };

        // RunToEndAsync blocks until the process terminates or times out.
        // The 5th argument wires the HitlExternalChannel so the proxy step can
        // call channel.EmitExternalEventAsync when HitlPauseStep fires.
        await LocalKernelProcessFactory.RunToEndAsync(
            process, kernel, initialEvent, TimeSpan.FromSeconds(60), hitlChannel);

        if (!hitlChannel.WasPaused)
        {
            // Process reached a terminal state (Ready → Estimation → Action, or Blocked).
            break;
        }

        // Extract the TicketState carried by the proxy message.
        // In the local runtime the data stays typed; defensively fall back to current state.
        var pausedTicket = ExtractTicketFromProxyMessage(hitlChannel.PausedMessage!, currentTicket);

        // Present the PO prompt and collect their answer.
        AnsiConsole.MarkupLine("\n  [bold yellow]⏸ Awaiting PO input[/] — please answer the clarifying questions above.");
        AnsiConsole.Markup("  [bold]Your answer:[/] ");
        var humanAnswer = Console.ReadLine() ?? string.Empty;
        AnsiConsole.WriteLine();

        currentTicket = pausedTicket with
        {
            HumanAnswer = humanAnswer,
            ClarificationRound = pausedTicket.ClarificationRound + 1,
        };

        AnsiConsole.MarkupLine(
            $"  [dim]↳ Clarification round {currentTicket.ClarificationRound} — re-validating with PO input.[/]");
        AnsiConsole.WriteLine();
    }

    AnsiConsole.MarkupLine("[green]✓ Pipeline complete.[/]");
}

/// <summary>
/// Extracts the TicketState from a KernelProcessProxyMessage.
/// KernelProcessEventData.ToObject() deserializes the JSON-serialized payload back
/// to its original type; we cast the result to TicketState.
/// </summary>
static TicketState ExtractTicketFromProxyMessage(KernelProcessProxyMessage message, TicketState fallback)
{
    if (message.EventData?.ToObject() is TicketState deserializedTicket)
        return deserializedTicket;

    return fallback;
}
