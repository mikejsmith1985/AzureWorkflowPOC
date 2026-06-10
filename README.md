# DBAIAzure — Ticket Intake Pipeline

A standalone .NET 8 demo of Semantic Kernel Process Framework with Anthropic Claude,
built as interview prep for Azure AI engineering roles.

## What this demonstrates

| Concept | Implementation |
|---------|---------------|
| SK Process Framework | `IntakePipelineBuilder` wires 6 steps with typed events — analogous to LangGraph `StateGraph` |
| HITL suspend/resume | `HitlPauseStep` → proxy step → `HitlExternalChannel`; runner re-injects `HumanResponded` |
| Structured LLM output | `ValidationStep` and `EstimationStep` parse typed JSON from Claude responses |
| Fibonacci estimation | `EstimationStep` uses anchor-based reference class forecasting (see below) |
| Azure Monitor tracing | `AddAzureMonitorTraceExporter` auto-instruments every SK call — visible in AI Foundry |
| Provider swap | One-line switch from Anthropic to Azure OpenAI via SK's `IChatCompletionService` |

## Architecture

```mermaid
flowchart TD
    A([Console input / ticket POCO]) --> B[IntakeStep\nnormalize title + description]
    B --> C[ValidationStep\nDoR check via Claude]
    C -- ready --> D[EstimationStep\nFibonacci sizing]
    C -- not ready --> E[GapAnalysisStep\ngenerate clarifying Qs]
    E --> F[HitlPauseStep\nemit AwaitHuman]
    F -. proxy + external channel .-> R[[Runner\nConsole.ReadLine]]
    R -. HumanResponded .-> C
    C -- blocked after 3 rounds --> X([Blocked])
    D --> G[ActionStep\nmock Jira create]
    G --> H([Done — Jira URL printed])
```

**HITL mechanism:** `HitlPauseStep` emits an `AwaitHuman` event that SK routes through a proxy
step to `HitlExternalChannel` (implements `IExternalKernelProcessMessageChannel`).
`RunToEndAsync` unblocks when the proxy fires; the runner collects console input, increments
`ClarificationRound`, and restarts the process with a `HumanResponded` event pointing directly
at `ValidationStep`. After 3 unsuccessful rounds, `ValidationStep` emits `Blocked` and the
process terminates without escalating further.

This is the .NET equivalent of LangGraph's `interrupt()` / `Command(resume=...)` — but compiled,
typed, and without a Python async context manager.

## Estimation: Fibonacci anchor table

`EstimationStep` uses reference class forecasting — Claude compares the ticket against known
anchor tasks instead of estimating in isolation. The reasoning is returned alongside the number,
making every estimate auditable.

| Points | Anchor |
|--------|--------|
| 1 | Add a null check or log statement |
| 2 | Add a new field to an existing model + migration |
| 3 | Implement a single new REST endpoint with tests |
| 5 | Build a new CRUD feature with validation logic |
| 8 | Build a new integration with an external system |
| 13 | Refactor a core subsystem or migrate a database schema |
| 21 | Architect a new major feature spanning multiple services |

If Claude returns a value outside the Fibonacci sequence, `EstimationStep` clamps it to the
nearest valid value via `ValidPoints.MinBy(p => Math.Abs(p - result.Points))`.

## Setup

### 1. Prerequisites

- .NET 8 SDK
- Anthropic API key (get one at console.anthropic.com)
- Optional: Azure Application Insights connection string for distributed tracing

### 2. Configure secrets

Create the Development settings file (gitignored — never committed):

```bash
cp src/DBAIAzure.Runner/appsettings.json src/DBAIAzure.Runner/appsettings.Development.json
```

Edit `appsettings.Development.json`:

```json
{
  "Anthropic": {
    "ApiKey": "sk-ant-...",
    "Model": "claude-sonnet-4-6"
  },
  "AzureMonitor": {
    "ConnectionString": "InstrumentationKey=...;IngestionEndpoint=..."
  }
}
```

`AzureMonitor.ConnectionString` is optional — the runner skips tracing gracefully if it is empty.

### 3. Run

```bash
dotnet run --project src/DBAIAzure.Runner
```

The runner processes two demo tickets in sequence:

- **INC0001001** — well-formed ticket; takes the happy path straight to Jira URL
- **INC0001002** — vague ticket ("Fix the thing with login"); triggers gap analysis, HITL pause,
  and console input collection before re-validating

### 4. Test

```bash
dotnet test
```

17 unit tests covering DoR JSON parsing, Fibonacci clamping, record immutability, and
`HitlExternalChannel` event routing — all pass without hitting a live LLM.

## Swapping to Azure OpenAI

The custom `AnthropicChatCompletionService` implements SK's `IChatCompletionService`.
To swap to Azure OpenAI, replace these lines in `Program.cs`:

```csharp
// Current — custom Anthropic service
kernelBuilder.Services.AddSingleton<IChatCompletionService>(
    new AnthropicChatCompletionService(anthropicKey, anthropicModel));
```

```csharp
// Azure OpenAI drop-in
kernelBuilder.AddAzureOpenAIChatCompletion(deployment, endpoint, apiKey);
```

No other changes required. The process steps, event routing, HITL logic, and observability
pipeline are completely unchanged — that is the SK abstraction working correctly.

## Solution structure

```
src/
  DBAIAzure.Core/        # Domain models (TicketState, DorVerdict) — no Azure deps
  DBAIAzure.Processes/   # SK Process steps, IntakePipelineBuilder, HitlExternalChannel
  DBAIAzure.Connectors/  # FakeTicketConnector (simulates ServiceNow read)
  DBAIAzure.Runner/      # Console entry point, DI wiring, HITL loop
tests/
  DBAIAzure.Tests/       # xUnit — pure-logic unit tests, no LLM required
```

## Interview talking points

| Concept | What to say |
|---------|-------------|
| SK PF vs LangGraph | "SK PF is .NET-native, event-driven, and production-grade. Steps communicate via strongly-typed events rather than a shared state dictionary." |
| HITL | "External events let the process suspend and resume across async boundaries via `IExternalKernelProcessMessageChannel` — same semantic as LangGraph `interrupt()` but compiled and typed." |
| Structured output | "`InvokePromptAsync` with a JSON schema instruction — same as LangChain's `with_structured_output`, but with C# records instead of Pydantic models." |
| Foundry tracing | "`AddAzureMonitorTraceExporter` auto-instruments every SK call — token counts, latency, and prompt/completion text visible in Foundry without a single manual span." |
| Provider swap | "Built against Anthropic for speed; swapping to Azure OpenAI is one line. That proves the SK abstraction is working correctly." |
| Fibonacci estimation | "Anchor-based reference class forecasting — Claude compares against known tasks instead of guessing in isolation. Every estimate comes with auditable reasoning, not just a number." |
