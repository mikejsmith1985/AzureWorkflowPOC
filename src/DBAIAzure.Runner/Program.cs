using Azure.Monitor.OpenTelemetry.Exporter;
using DBAIAzure.Connectors.Ai;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.Ai;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
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
// Model-call and workflow spans auto-trace to Azure Monitor via the MAF / Microsoft.Extensions.AI sources.
TracerProvider? tracerProvider = null;
if (!string.IsNullOrWhiteSpace(azureMonitorCs) && !azureMonitorCs.StartsWith("REPLACE"))
{
    tracerProvider = Sdk.CreateTracerProviderBuilder()
        .AddSource(DBAIAzure.Core.Diagnostics.AiTelemetrySourceNames.ChatClient) // MAF/M.E.AI model-call spans
        .AddSource(DBAIAzure.Core.Diagnostics.AiTelemetrySourceNames.Agents)     // MAF workflow/agent spans
        .AddAzureMonitorTraceExporter(o => o.ConnectionString = azureMonitorCs)
        .Build();
    AnsiConsole.MarkupLine("[dim]Azure Monitor tracing enabled.[/]");
}
else
{
    AnsiConsole.MarkupLine("[dim]Azure Monitor not configured — set AzureMonitor:ConnectionString to enable.[/]");
}

// ── Model client (provider-neutral IChatClient over Claude) ────────────────────
// To swap providers, build a different IChatClientProvider (e.g. OpenAiChatClientProvider) — the workflow,
// executors, routing, and observability are completely unchanged.
IChatClient chatClient = new AnthropicChatClientProvider()
    .Create(new AiProviderConfig(AiProviderConfig.DefaultProviderId, anthropicModel, anthropicKey));

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

AnsiConsole.Write(new Rule("[bold blue]DBAIAzure — Ticket Intake Pipeline (MAF Workflows)[/]"));
AnsiConsole.WriteLine();

foreach (var ticket in demoTickets)
{
    await RunTicketAsync(chatClient, ticket);
    AnsiConsole.WriteLine();
}

tracerProvider?.Dispose();

// ── Per-ticket runner ──────────────────────────────────────────────────────────
// HITL loop on MAF Workflows: drive the intake workflow to completion or to the clarification RequestPort,
// collect Console.ReadLine() at each pause, respond, and drive again. The ValidationExecutor blocks after
// its max clarification round, ending the loop.
static async Task RunTicketAsync(IChatClient chatClient, TicketState ticket)
{
    AnsiConsole.Write(new Rule($"[yellow]{ticket.TicketId}[/] {Markup.Escape(ticket.Title)}"));

    var workflow = MafIntakeWorkflowFactory.Build(chatClient);
    var session = await MafWorkflowSession<TicketState>.StartAsync(
        workflow, ticket, ticket.TicketId, checkpointManager: null, CancellationToken.None);

    while (true)
    {
        var segment = await session.DriveAsync(CancellationToken.None);
        if (!segment.Suspended)
        {
            break; // Ready → Estimation → Action, or Blocked — a terminal state.
        }

        // Parked at the clarification gate: surface the questions and collect the PO's answer.
        var pausedTicket = ExtractTicket(segment.PendingRequest!, ticket);
        if (pausedTicket.ClarifyingQuestions.Count > 0)
        {
            AnsiConsole.MarkupLine("\n  [bold]Clarifying questions:[/]");
            foreach (var question in pausedTicket.ClarifyingQuestions)
                AnsiConsole.MarkupLine($"    • {Markup.Escape(question)}");
        }

        AnsiConsole.MarkupLine("\n  [bold yellow]⏸ Awaiting PO input[/]");
        AnsiConsole.Markup("  [bold]Your answer:[/] ");
        var humanAnswer = Console.ReadLine() ?? string.Empty;
        AnsiConsole.WriteLine();

        // Apply the answer and clear the now-answered questions so validation's ready/not-ready routing
        // (keyed on whether the ticket still carries questions) evaluates the re-validation cleanly.
        var answeredTicket = pausedTicket with
        {
            HumanAnswer = humanAnswer,
            ClarificationRound = pausedTicket.ClarificationRound + 1,
            ClarifyingQuestions = [],
        };

        AnsiConsole.MarkupLine(
            $"  [dim]↳ Clarification round {answeredTicket.ClarificationRound} — re-validating with PO input.[/]");
        AnsiConsole.WriteLine();

        await session.RespondAsync(segment.PendingRequest!.Request, answeredTicket, CancellationToken.None);
    }

    AnsiConsole.MarkupLine("[green]✓ Pipeline complete.[/]");
}

/// <summary>Reads the paused ticket carried by the clarification request, falling back to the last state.</summary>
static TicketState ExtractTicket(RequestInfoEvent request, TicketState fallback) =>
    request.Request.TryGetDataAs<TicketState>(out var ticket) && ticket is not null ? ticket : fallback;
