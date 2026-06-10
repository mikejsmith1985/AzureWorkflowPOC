# Changelog — AzureWorkflowPOC

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed
- Proxy step name changed from `"hitl-proxy"` to `"hitl_proxy"` — SK rejects plugin names containing hyphens

### Added
- README: architecture Mermaid diagram, Fibonacci anchor table, setup instructions, provider swap guide, and interview talking points
- HITL resume loop: `HitlExternalChannel` implements `IExternalKernelProcessMessageChannel`; receives `AwaitHuman` via a proxy step and lets the runner collect `Console.ReadLine()` before restarting the process with `HumanResponded`
- Proxy step in `IntakePipelineBuilder` (`AddProxyStep` + `EmitExternalEvent`) routes the internal `AwaitHuman` event out of the process boundary — the SK PF equivalent of LangGraph's `interrupt()`
- Runner `RunTicketAsync` loops up to 3 clarification rounds, matching `ValidationStep`'s `ClarificationRound >= 3 → Blocked` cap
- Spectre.Console output in every step: intake normalisation, DoR verdict with reasoning, Fibonacci estimate with anchor justification, gap-analysis questions, HITL pause banner, and final summary table (ticket ID, story points, Jira URL)
- `LocalKernelProcessFactory.RunToEndAsync` replaces `StartAsync` — process now blocks until all async steps complete before returning
- Model updated from deprecated `claude-3-5-sonnet-20241022` to `claude-sonnet-4-6` in `appsettings.json`

### Fixed
- Happy-path steps were silently running fire-and-forget; `RunToEndAsync` ensures the runner waits for process completion before printing results

### Previous
- Full .NET 8 solution: DBAIAzure.Core, Processes, Connectors, Runner, Tests
- SK Process Framework intake pipeline with 6 steps (IntakeStep → ValidationStep → GapAnalysisStep → HitlPauseStep → EstimationStep → ActionStep)
- Custom IChatCompletionService backed by raw Anthropic Messages API (HttpClient, no SDK dependency)
- Azure Monitor OTLP tracing via AddAzureMonitorTraceExporter — all SK calls auto-traced
- HITL suspend/resume via SK external events (HitlPauseStep + HumanResponded)
- Fibonacci estimation with anchor-based reference class forecasting (EstimationStep)
- 13 passing xUnit tests covering DoR parsing, Fibonacci clamping, and record immutability
- Forge Workflow initialized with Forge Terminal Workflow Architect
