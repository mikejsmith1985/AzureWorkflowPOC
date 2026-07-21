using Microsoft.AspNetCore.DataProtection;
using System.Text.Json;
using DBAIAzure.Connectors;
using DBAIAzure.Core.Configuration;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Services;
using DBAIAzure.Core.Validation;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Repositories;
using DBAIAzure.Web.Hubs;
using DBAIAzure.Web.Integrations.AzureDevOps;
using DBAIAzure.Web.Integrations.SpecKit;
using DBAIAzure.Web.Integrations.Teams;
using DBAIAzure.Web.Rules;
using DBAIAzure.Web.Services;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;


var builder = WebApplication.CreateBuilder(args);

// ── Key Vault (T066): when KeyVault:Uri is set, overlay secrets from Azure Key Vault.
// Credentials resolve via DefaultAzureCredential (managed identity in production, developer
// credentials locally). Connector secrets referenced as "Connectors:<name>:<field>" in config
// map transparently to Key Vault secrets with the same name (hyphens replace colons per KV rules).
var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
{
    var credential = new Azure.Identity.DefaultAzureCredential();
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), credential);
}

// ── Configuration ──────────────────────────────────────────────────────────────
var anthropicKey   = builder.Configuration["Anthropic:ApiKey"]   ?? string.Empty;
var anthropicModel = builder.Configuration["Anthropic:Model"]    ?? "claude-sonnet-4-6";
var portalBaseUrl  = builder.Configuration["Portal:BaseUrl"]     ?? "http://localhost:5000";
var dbPath         = builder.Configuration["Storage:SqlitePath"] ?? "pipeline.db";

if (string.IsNullOrWhiteSpace(anthropicKey) || anthropicKey.StartsWith("REPLACE"))
    builder.Logging.AddFilter(logLevel => logLevel >= LogLevel.Warning); // Warn only — key may come from modal

// ── Blazor + MVC (for webhook controller) ─────────────────────────────────────
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddControllers();

// ── SQLite persistence ─────────────────────────────────────────────────────────
var dbFolder = Path.IsPathRooted(dbPath) ? dbPath : Path.Combine(builder.Environment.ContentRootPath, dbPath);
builder.Services.AddDbContextFactory<PipelineDbContext>(options =>
    options.UseSqlite($"Data Source={dbFolder}"));
// SqliteRunRepository depends only on IDbContextFactory (singleton) and creates its own
// short-lived DbContext per call, so singleton lifetime is safe and avoids captive-dependency errors.
builder.Services.AddSingleton<IRunRepository, SqliteRunRepository>();

// ── Data Protection — encrypts connector secrets at rest (T001, FR-019) ───────
// The key ring location is configurable via DataProtection:KeyRingPath. In the container it points at
// a writable, EPHEMERAL path (set in the Dockerfile) so keys live only for the container lifetime —
// every cold start resets them along with the rest of the demo state (FR-016). Locally it falls back
// to %APPDATA% so keys survive dev restarts. SetApplicationName is pinned so keys stay valid within a
// lifetime/deployment.
var configuredKeyRingPath = builder.Configuration["DataProtection:KeyRingPath"];
var dpKeyDir = new DirectoryInfo(!string.IsNullOrWhiteSpace(configuredKeyRingPath)
    ? configuredKeyRingPath
    : Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AzureWorkflowPOC", "DataProtection-Keys"));
dpKeyDir.Create();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(dpKeyDir)
    .SetApplicationName("AzureWorkflowPOC");
builder.Services.AddSingleton<ISecretProtector, DataProtectorAdapter>();

// ── Connector config repository (T013) ────────────────────────────────────────
builder.Services.AddSingleton<IConnectorConfigRepository, SqliteConnectorConfigRepository>();

// ── Demo connector seeding (012 US5) ──────────────────────────────────────────
// Bind the vault-injected seed values and register the boot seeder that pre-wires the demo's
// back-office connectors (ServiceNow/AzureDevOps/Messaging) on each cold start. Never seeds the LLM.
builder.Services.Configure<ConnectorSeedOptions>(builder.Configuration.GetSection(ConnectorSeedOptions.SectionName));
builder.Services.AddSingleton<DemoConnectorSeeder>();
builder.Services.AddSingleton<LegacyExampleWorkflowPurger>();

// ── Workflow persistence ────────────────────────────────────────────────────
builder.Services.AddSingleton<IWorkflowRepository, SqliteWorkflowRepository>();

// ── Repo-app registry + monitoring heartbeat store (feature 013) ───────────────
// Both depend only on IDbContextFactory and create their own short-lived DbContext per call,
// so singleton lifetime is safe (no captive dependency).
builder.Services.AddSingleton<IAppRegistryRepository, SqliteAppRegistryRepository>();
builder.Services.AddSingleton<IAppHeartbeatStore, SqliteAppHeartbeatStore>();
// In-process status notifier for live Apps-page updates (mirrors the orchestrator's RunUpdated).
builder.Services.AddSingleton<IAppStatusNotifier, DBAIAzure.Web.Services.AppStatusNotifier>();
// The active executor is chosen at first use: a real Docker executor when an engine is reachable and
// demo mode is off, otherwise the simulated executor — identical surfaces either way (US4, FR-015).
builder.Services.AddSingleton<IAppExecutor>(sp =>
{
    var registry = sp.GetRequiredService<IAppRegistryRepository>();
    var notifier = sp.GetRequiredService<IAppStatusNotifier>();
    var sim = new DBAIAzure.Connectors.Apps.SimAppExecutor(registry, notifier);

    var demoMode = builder.Configuration.GetValue<bool>("Apps:DemoMode");
    Docker.DotNet.IDockerClient? dockerClient = null;
    var dockerAvailable = !demoMode
        && DBAIAzure.Connectors.Apps.AppExecutorSelector.TryConnectDocker(out dockerClient)
        && dockerClient is not null;
    DBAIAzure.Core.Interfaces.IAppExecutor docker = dockerAvailable
        ? new DBAIAzure.Connectors.Apps.DockerAppExecutor(registry, notifier, dockerClient!)
        : sim;
    return DBAIAzure.Connectors.Apps.AppExecutorSelector.Select(dockerAvailable, demoMode, docker, sim);
});
// App monitoring: a chosen saved workflow runs (via the existing orchestrator) when a monitored app's
// snapshot indicates a problem; a hosted loop cycles linked apps and records heartbeats (feature 013).
builder.Services.AddSingleton<IAppMonitoringService, DBAIAzure.Processes.Monitoring.AppMonitoringService>();
builder.Services.AddHostedService<DBAIAzure.Web.Services.AppMonitoringBackgroundService>();

// ── ServiceNow outbound HTTP client — 35 s timeout, base URL set per-request (T002) ─
builder.Services.AddHttpClient(nameof(ServiceNowClient), client =>
{
    client.Timeout = TimeSpan.FromSeconds(35);
});

// ── Messaging connector — webhook delivery profiles + delivery seam (010 US1) ─────
// One profile per platform; MessageDelivery resolves config/secrets per call and chooses the path.
builder.Services.AddHttpClient(nameof(DBAIAzure.Connectors.Messaging.MessageDelivery));
builder.Services.AddSingleton<DBAIAzure.Connectors.Messaging.IPlatformWebhookProfile,
    DBAIAzure.Connectors.Messaging.TeamsWebhookProfile>();
builder.Services.AddSingleton<DBAIAzure.Connectors.Messaging.IPlatformWebhookProfile,
    DBAIAzure.Connectors.Messaging.SlackWebhookProfile>();
builder.Services.AddSingleton<DBAIAzure.Connectors.Messaging.IPlatformWebhookProfile,
    DBAIAzure.Connectors.Messaging.DiscordWebhookProfile>();
// MCP-first delivery gateway (official MCP client SDK over HTTP/SSE); MessageDelivery uses it when an
// MCP server endpoint is configured and falls back to the webhook profiles otherwise (010 US3).
builder.Services.AddSingleton<DBAIAzure.Connectors.Messaging.IMcpMessageGateway,
    DBAIAzure.Connectors.Messaging.McpMessageGateway>();
builder.Services.AddSingleton<IMessageDelivery, DBAIAzure.Connectors.Messaging.MessageDelivery>();

// ── HITL notifier — delivers pause-for-input notifications via the Messaging connector (010 US2) ─
builder.Services.AddSingleton<IHitlNotifier, DBAIAzure.Web.Integrations.Messaging.MessagingHitlNotifier>();

// ── Pipeline orchestrator (runs the ticket-intake pipeline on MAF Workflows) ────
builder.Services.AddSingleton<PipelineOrchestrator>(sp =>
{
    var chatClient        = sp.GetRequiredService<Microsoft.Extensions.AI.IChatClient>();
    var repo              = sp.GetRequiredService<IRunRepository>();
    var notifier          = sp.GetService<IHitlNotifier>();
    var healthChecker     = sp.GetService<IConnectorHealthChecker>();
    var checkpointManager = sp.GetService<Microsoft.Agents.AI.Workflows.CheckpointManager>();

    return new PipelineOrchestrator(
        chatClient, repo, notifier, portalBaseUrl, healthChecker, checkpointManager);
});

// ── Spec Kit phase handler (parallel track — does not touch the ticket pipeline) ─
builder.Services.Configure<SpecKitOptions>(builder.Configuration.GetSection(SpecKitOptions.SectionName));
builder.Services.Configure<AzureDevOpsOptions>(builder.Configuration.GetSection(AzureDevOpsOptions.SectionName));

// Phase-run persistence — same singleton-safe DbContextFactory pattern as the ticket repository.
builder.Services.AddSingleton<IPhaseRunRepository, SqlitePhaseRunRepository>();

// Seams resolved as app-level singletons; the kernel factory injects them per run.
builder.Services.AddSingleton<IArtifactReader, FileSystemArtifactReader>();

// IBoardsClient — AzureDevOpsBoardsClient accepts optional IConnectorConfigRepository for hot-reload (FR-014).
builder.Services.AddSingleton<IBoardsClient, AzureDevOpsBoardsClient>();

// Decision-card notifier — named HttpClient, fire-and-forget (mirrors the Teams notifier).
var decisionCardUrl = builder.Configuration["SpecKit:DecisionCardUrl"] ?? string.Empty;
builder.Services.AddHttpClient(nameof(ForgeApprovalNotifier), client =>
{
    if (!string.IsNullOrWhiteSpace(decisionCardUrl))
        client.BaseAddress = new Uri(decisionCardUrl);
});
builder.Services.AddSingleton<IPhaseApprovalNotifier>(sp =>
    new ForgeApprovalNotifier(sp.GetRequiredService<IHttpClientFactory>(),
                              sp.GetRequiredService<ILogger<ForgeApprovalNotifier>>()));

// ── AI cost tracking (spec-017): minter, ledger, binding→work-item resolution map ────────────
builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.IBindingKeyMinter,
    DBAIAzure.Web.Services.BindingKeyMinter>();
builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.ICostLedger,
    DBAIAzure.Storage.Repositories.SqlCostLedger>();
builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.IBindingWorkItemMap,
    DBAIAzure.Storage.Repositories.SqlBindingWorkItemMap>();
builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.ICostProjection,
    DBAIAzure.Web.Services.CostProjectionService>();

// ── Work Tracking System config resolver (spec-020) ───────────────────────────────────────────
// Reads the active WorkTracker connector (provider + credentials) from the store per run. Additive:
// registered ahead of the consumers (adapter provider, Jira connection factory, testers) that the
// generic-connector increment wires onto it.
builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.IWorkTrackerConfigResolver,
    DBAIAzure.Web.Services.WorkTrackerConfigResolver>();

// ── DoR Validation Workflow (spec-021): per-run config resolver + durable instance store ──────
// Reads the DorWorkflow connector row (six namespaces + secrets) on every call so operator changes apply
// without a restart; the instance store persists the queryable lifecycle/SLA record the sweeper reads.
builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.IDorConfigResolver,
    DBAIAzure.Web.Services.Dor.DorConfigResolver>();
builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.IDorWorkflowInstanceStore,
    DBAIAzure.Storage.Repositories.EfDorWorkflowInstanceStore>();
builder.Services.AddHttpClient(nameof(DBAIAzure.Web.Services.Dor.DorDocumentSource));
builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.IDorDocumentSource,
    DBAIAzure.Web.Services.Dor.DorDocumentSource>();
builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.IDorReviewService,
    DBAIAzure.Processes.Executors.Dor.DorReviewService>();
builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.IDorConversationService,
    DBAIAzure.Processes.Executors.Dor.DorConversationService>();
// The orchestrator lives in the Processes layer, so it takes IWorkTrackerAdapter; we pass the ActiveWorkTracker
// adapter (resolved per run) explicitly here so the active provider (Jira) is used without leaking a Web type.
// The CheckpointManager (when present) makes a paused conversation resumable after a restart.
builder.Services.AddSingleton<DBAIAzure.Processes.Pipeline.DorWorkflowOrchestrator>(sp =>
    new DBAIAzure.Processes.Pipeline.DorWorkflowOrchestrator(
        sp.GetRequiredService<DBAIAzure.Core.Interfaces.IDorReviewService>(),
        sp.GetRequiredService<DBAIAzure.Core.Interfaces.IDorConversationService>(),
        sp.GetRequiredService<DBAIAzure.Web.Services.ActiveWorkTrackerAdapter>(),
        sp.GetRequiredService<DBAIAzure.Core.Interfaces.IDorDocumentSource>(),
        sp.GetRequiredService<DBAIAzure.Core.Interfaces.IDorConfigResolver>(),
        sp.GetRequiredService<DBAIAzure.Core.Interfaces.IMessageDelivery>(),
        sp.GetRequiredService<DBAIAzure.Core.Interfaces.IDorWorkflowInstanceStore>(),
        sp.GetRequiredService<ILogger<DBAIAzure.Processes.Pipeline.DorWorkflowOrchestrator>>(),
        sp.GetService<Microsoft.Agents.AI.Workflows.CheckpointManager>()));

// ── Work-tracker adapter (spec-018): tracker-neutral seam + ADO implementation ───────────────
// Additive — the adapter wraps the existing ADO client/preflight; the pipeline is not yet rewired
// onto it (that is a later increment), so live ADO behaviour is unchanged. Scoped because the ADO
// adapter depends on the scoped IAdoTelemetryPreflightService.
builder.Services.AddSingleton<DBAIAzure.Web.Integrations.AzureDevOps.AdoFieldReferenceResolver>();
builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.IWorkTrackerAdapter,
    DBAIAzure.Web.Integrations.AzureDevOps.AzureDevOpsWorkTrackerAdapter>();

// ── Jira work-tracker adapter (spec-018 increment 3; spec-020 per-run credentials) ───────────────────
// The Jira connection factory resolves credentials from the connector store on each call and rebuilds the
// authed client only when they change (hot-reload — FR-005), replacing the former startup-baked HttpClient.
builder.Services.AddSingleton<DBAIAzure.Web.Integrations.Jira.IJiraConnectionFactory,
    DBAIAzure.Web.Integrations.Jira.JiraConnectionFactory>();
builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.IWorkTrackerAdapter>(sp =>
    new DBAIAzure.Web.Integrations.Jira.JiraWorkTrackerAdapter(
        sp.GetRequiredService<DBAIAzure.Web.Integrations.Jira.IJiraConnectionFactory>(),
        sp.GetRequiredService<DBAIAzure.Core.Interfaces.IBindingWorkItemMap>(),
        sp.GetRequiredService<ILogger<DBAIAzure.Web.Integrations.Jira.JiraWorkTrackerAdapter>>()));

builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.IWorkTrackerAdapterProvider,
    DBAIAzure.Web.Services.WorkTrackerAdapterProvider>();

// Routing adapter for components that hold a single adapter for their lifetime (the singleton orchestrator):
// forwards each call to whichever provider is active now, so a UI provider switch applies without a restart.
builder.Services.AddSingleton<DBAIAzure.Web.Services.ActiveWorkTrackerAdapter>();

builder.Services.AddSingleton<PhaseHandlerOrchestrator>(sp =>
{
    var chatClient     = sp.GetRequiredService<Microsoft.Extensions.AI.IChatClient>();
    var artifactReader = sp.GetRequiredService<IArtifactReader>();
    var phaseRepo      = sp.GetRequiredService<IPhaseRunRepository>();
    var bindingKeyMinter   = sp.GetRequiredService<DBAIAzure.Core.Interfaces.IBindingKeyMinter>();
    // Route board writes through the active-provider adapter so a UI tracker switch applies per run (spec-020).
    var workTrackerAdapter = sp.GetRequiredService<DBAIAzure.Web.Services.ActiveWorkTrackerAdapter>();

    // The board-write dependencies the create executor needs, resolved from DI (the cost/telemetry ones are
    // best-effort and may be absent). Replaces the old per-run SK kernel container.
    var writerDeps = new PhaseWorkItemWriterDeps(
        Tracker:         workTrackerAdapter,
        Repository:      phaseRepo,
        BindingMap:      sp.GetService<DBAIAzure.Core.Interfaces.IBindingWorkItemMap>(),
        Ledger:          sp.GetService<DBAIAzure.Core.Interfaces.ICostLedger>(),
        TelemetrySource: sp.GetService<DBAIAzure.Core.Interfaces.IRunTelemetrySource>(),
        Projection:      sp.GetService<DBAIAzure.Core.Interfaces.ICostProjection>(),
        WriteBack:       sp.GetService<DBAIAzure.Core.Interfaces.ITelemetryWriteBack>());

    var notifier          = sp.GetService<IPhaseApprovalNotifier>();
    var healthChecker     = sp.GetService<IConnectorHealthChecker>();
    var checkpointManager = sp.GetService<Microsoft.Agents.AI.Workflows.CheckpointManager>();

    return new PhaseHandlerOrchestrator(
        chatClient, artifactReader, writerDeps, phaseRepo, notifier, portalBaseUrl, healthChecker,
        bindingKeyMinter, checkpointManager);
});

// ── Connector health checker + per-connector testers (T020) ───────────────────
builder.Services.AddSingleton<ServiceNowClient>();
builder.Services.AddSingleton<AdoConnectorTester>();
builder.Services.AddSingleton<JiraConnectorTester>();
builder.Services.AddSingleton<LlmConnectorTester>();
builder.Services.AddSingleton<MessagingConnectorTester>();
builder.Services.AddSingleton<DorWorkflowTester>();   // spec-021 DoR workflow health
builder.Services.AddSingleton<IConnectorHealthChecker, ConnectorHealthChecker>();

// ── Admin Console UX (spec-009) ───────────────────────────────────────────────
// Scoped (per Blazor circuit): reads browser localStorage + runs an LLM health check to drive the
// first-run onboarding banner; the field-tooltip portal service holds the active tooltip per session.
builder.Services.AddScoped<IOnboardingStateService, OnboardingStateService>();
builder.Services.AddScoped<ITooltipService, TooltipService>();
// Per-circuit shell presentation preferences (text size + Assistant panel open/closed), persisted in
// browser localStorage. Scoped so each visitor session restores its own choices on first render.
builder.Services.AddScoped<IUiPreferenceService, UiPreferenceService>();

// ── Workflow run repository (FR-18, US1) ───────────────────────────────────────
builder.Services.AddSingleton<IWorkflowRunRepository, EfWorkflowRunRepository>();

// ── Workflow execution event observers (FR-21, US4) ───────────────────────────
builder.Services.AddSingleton<IWorkflowObserver, SqlWorkflowObserver>();
builder.Services.AddSingleton<IWorkflowObserver, SignalRWorkflowObserver>();
builder.Services.AddSingleton<IWorkflowObserver, AzureMonitorWorkflowObserver>();

// Single LLM-usage capture point — records each call's tokens/cache/errors as a run-correlated event
// (covers both runner and phase-handler paths; injected into the Anthropic connector below).
builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.ILlmUsageReporter,
    DBAIAzure.Web.Services.LlmUsageReporter>();

// ── MAF model layer (spec-019 T012/T022): provider-neutral IChatClient pipeline ──
// provider registry → HotReloadChatClient (re-resolves the LLM key/model from the DB per call) →
// CostCapturingChatClient (feeds the existing usage reporter, so the cost ledger is unchanged). Additive:
// the SK chat services below still back the current pipelines until the atomic cutover (FR-003).
// Both built-in providers are registered; the active one is selected by AI:Provider (default anthropic) —
// adding a provider is one registration, with no change to any pipeline or executor (spec-019 T042/T043).
builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.IChatClientProvider,
    DBAIAzure.Connectors.Ai.AnthropicChatClientProvider>();
builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.IChatClientProvider,
    DBAIAzure.Connectors.Ai.OpenAiChatClientProvider>();
builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.IChatClientProviderRegistry>(sp =>
    new DBAIAzure.Connectors.Ai.ChatClientProviderRegistry(
        sp.GetServices<DBAIAzure.Core.Interfaces.IChatClientProvider>()));
builder.Services.AddSingleton<Microsoft.Extensions.AI.IChatClient>(sp =>
{
    var registry = sp.GetRequiredService<DBAIAzure.Core.Interfaces.IChatClientProviderRegistry>();
    var configRepo = sp.GetRequiredService<IConnectorConfigRepository>();
    var usageReporter = sp.GetRequiredService<DBAIAzure.Core.Interfaces.ILlmUsageReporter>();
    var costLogger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<DBAIAzure.Web.Services.Ai.CostCapturingChatClient>();

    // The active provider is chosen by AI:Provider (default anthropic, per-instance — spec-019 T042). A
    // non-default provider reads its key/model/endpoint from AI:<Provider>:* configuration (secret by
    // reference); the default keeps the DB-hot-reload path so the visitor-supplied Claude key powers everything.
    var activeProviderId = (builder.Configuration["AI:Provider"] ?? DBAIAzure.Core.Models.Ai.AiProviderConfig.DefaultProviderId)
        .Trim().ToLowerInvariant();

    DBAIAzure.Core.Models.Ai.AiProviderConfig ResolveActiveConfig()
    {
        // Non-default providers are configured purely from AI:<Provider>:* (no legacy DB LLM row).
        if (activeProviderId != DBAIAzure.Core.Models.Ai.AiProviderConfig.DefaultProviderId)
        {
            var section = builder.Configuration.GetSection($"AI:{activeProviderId}");
            return new DBAIAzure.Core.Models.Ai.AiProviderConfig(
                activeProviderId,
                section["Model"] ?? string.Empty,
                section["ApiKey"] ?? string.Empty,
                Endpoint: section["Endpoint"]);
        }

        // Default (Claude): re-resolve key + model from the DB LLM connector on each call (config fallback),
        // mirroring the per-run kernel factory so the single visitor-supplied key powers every model call.
        var effectiveKey = anthropicKey;
        var effectiveModel = anthropicModel;
        try
        {
            var configResult = configRepo.GetAsync(ConnectorType.LLM).GetAwaiter().GetResult();
            if (configResult?.NonSecretConfig is { } nsJson)
            {
                using var nsDoc = JsonDocument.Parse(nsJson);
                if (nsDoc.RootElement.TryGetProperty("modelName", out var mProp) && !string.IsNullOrEmpty(mProp.GetString()))
                    effectiveModel = mProp.GetString()!;
            }
            var secretsJson = configRepo.GetDecryptedSecretsAsync(ConnectorType.LLM).GetAwaiter().GetResult();
            if (secretsJson is not null)
            {
                using var sDoc = JsonDocument.Parse(secretsJson);
                if (sDoc.RootElement.TryGetProperty("apiKey", out var kProp) && !string.IsNullOrEmpty(kProp.GetString()))
                    effectiveKey = kProp.GetString()!;
            }
        }
        catch
        {
            // DB not available or no LLM config yet — fall back to IConfiguration values.
        }
        return new DBAIAzure.Core.Models.Ai.AiProviderConfig(
            DBAIAzure.Core.Models.Ai.AiProviderConfig.DefaultProviderId, effectiveModel, effectiveKey);
    }

    var hotReload = new DBAIAzure.Connectors.Ai.HotReloadChatClient(registry, ResolveActiveConfig);
    var cost = new DBAIAzure.Web.Services.Ai.CostCapturingChatClient(hotReload, usageReporter, costLogger);

    // spec-019 T013/T049: emit gen_ai model-call spans under the MAF/M.E.AI source so they reach Azure
    // Monitor via the OTel exporter (replaces the SK telemetry filters as the model-call trace source).
    var otelLogger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Microsoft.Extensions.AI.OpenTelemetryChatClient");
    return new Microsoft.Extensions.AI.OpenTelemetryChatClient(
        cost, otelLogger, DBAIAzure.Core.Diagnostics.AiTelemetrySourceNames.ChatClient);
});

// ── Durable MAF checkpointing (spec-019 T030/T032) ────────────────────────────
// The EF-backed checkpoint store + JSON manager let MAF runs paused at a HITL gate resume after a
// restart. Passed to the orchestrators below so their runs are checkpointed when the MAF flag is on.
builder.Services.AddSingleton<DBAIAzure.Storage.Checkpointing.EfCheckpointStore>();
builder.Services.AddSingleton<Microsoft.Agents.AI.Workflows.CheckpointManager>(sp =>
    Microsoft.Agents.AI.Workflows.CheckpointManager.CreateJson(
        sp.GetRequiredService<DBAIAzure.Storage.Checkpointing.EfCheckpointStore>(),
        new System.Text.Json.JsonSerializerOptions()));

// ── WorkflowApprovalNotifier — Teams notification on HITL pause (FR-19, US2) ──
// Registered as null-safe stub; TeamsWorkflowApprovalNotifier wired in US2 implementation phase.
builder.Services.AddSingleton<IWorkflowApprovalNotifier, TeamsWorkflowApprovalNotifier>();

// ── DoR validation framework (FR-24, US7) ─────────────────────────────────────
builder.Services.Configure<DorRuleSettings>(builder.Configuration.GetSection(DorRuleSettings.SectionName));
builder.Services.AddSingleton<IWorkflowReadinessRule, TriggerNodePresentRule>();
builder.Services.AddSingleton<IWorkflowReadinessRule, AllNodesRealizedRule>();
builder.Services.AddSingleton<IWorkflowReadinessRule, ConnectorsHealthyRule>();
builder.Services.AddSingleton<IWorkflowReadinessRule, ApprovalNodesConfiguredRule>();
builder.Services.AddSingleton<IWorkflowPreRunValidator, WorkflowPreRunValidator>();

// ── Run retention background service (FR-18.4) ────────────────────────────────
builder.Services.AddHostedService<WorkflowRunRetentionService>();

// ── Startup rehydration of Paused runs (FR-18.5, T031) ───────────────────────
builder.Services.AddHostedService<WorkflowRunRehydrationService>();

// ── Startup rehydration of MAF-paused intake runs (spec-019 T032) ─────────────
// Resumes intake runs left awaiting-human from their durable checkpoints after a restart (MAF flag only).
builder.Services.AddHostedService<PausedRunRehydrationService>();

// ── Startup rehydration of paused DoR conversations (spec-021 SC-003) ─────────
// Resumes DoR runs left awaiting a human reply from their durable checkpoints after a restart.
builder.Services.AddHostedService<DBAIAzure.Web.Services.Dor.DorRunRehydrationService>();

// ── DoR conversation reply pump (spec-021 US2) ────────────────────────────────
// Polls awaiting-human DoR instances and feeds new Slack thread replies into the orchestrator.
builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.IChatReplyReader,
    DBAIAzure.Web.Integrations.Messaging.SlackMcpReplyReader>();
builder.Services.AddHostedService<DBAIAzure.Web.Services.Dor.DorReplyPumpService>();

// ── DoR SLA sweeper (spec-021 US3) ────────────────────────────────────────────
// Enforces the primary/escalation SLAs on a durable schedule: escalates or hands off on breach.
builder.Services.AddHostedService<DBAIAzure.Web.Services.Dor.DorSlaSweeperService>();

// ── Application Insights (FR-21, US4) ─────────────────────────────────────────
builder.Services.AddApplicationInsightsTelemetry(builder.Configuration);

// ── Workflow structural validator (spec 004) ───────────────────────────────────
builder.Services.AddSingleton<IWorkflowValidator, WorkflowValidator>();

// ── Design-time LLM services (Workflow Builder assistant + Node Realization) ───────────
// The builder assistant and node realization use the same provider-neutral IChatClient pipeline as the
// pipelines (hot-reloads the visitor-entered key from the DB, cost-metered, OTel-traced).
builder.Services.AddSingleton<WorkflowTopologySerializer>();
builder.Services.AddSingleton<ILlmAvailabilityMonitor, LlmAvailabilityMonitor>();
builder.Services.AddSingleton<IWorkflowCodeGenerator, WorkflowCodeGenerator>();
builder.Services.AddSingleton<WorkflowDesignSkillService>();
builder.Services.AddSingleton<IWorkflowThumbnailGenerator, WorkflowThumbnailGenerator>();
builder.Services.AddSingleton<IWorkflowCodeDiffService, WorkflowCodeDiffService>();
// WorkflowBuilderService is scoped (one instance per session / per page).
builder.Services.AddScoped<WorkflowBuilderService>();

// ── ADO Telemetry Preflight (spec-009) ────────────────────────────────────────
// Named HttpClient for the preflight service — reuses the ADO-scoped client pattern.
builder.Services.AddHttpClient(nameof(DBAIAzure.Web.Integrations.AzureDevOps.AdoTelemetryPreflightService),
    client => client.Timeout = TimeSpan.FromSeconds(30));
// Singleton (stateless path resolver) so the telemetry write-back — itself injected into the
// singleton phase-handler kernel — can depend on it without a scoped-from-root capture.
builder.Services.AddSingleton<DBAIAzure.Web.Integrations.AzureDevOps.ManifestPathResolver>();
builder.Services.AddScoped<DBAIAzure.Core.Interfaces.IAdoTelemetryPreflightService,
    DBAIAzure.Web.Integrations.AzureDevOps.AdoTelemetryPreflightService>();

// ── ADO Telemetry write-back (pushes a run's AI telemetry onto the work item it produced) ─────
builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.IRunTelemetrySource,
    DBAIAzure.Storage.Repositories.SqlRunTelemetrySource>();
builder.Services.AddSingleton<DBAIAzure.Web.Integrations.AzureDevOps.IAdoTelemetryManifestReader,
    DBAIAzure.Web.Integrations.AzureDevOps.AdoTelemetryManifestReader>();
builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.ITelemetryWriteBack,
    DBAIAzure.Web.Services.TelemetryWriteBackService>();

// ── LLM model fetcher — live model list from Anthropic / OpenAI ───────────────
builder.Services.AddHttpClient(nameof(DBAIAzure.Web.Integrations.LLM.LlmModelFetcherService),
    client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<DBAIAzure.Web.Integrations.LLM.ILlmModelFetcherService,
    DBAIAzure.Web.Integrations.LLM.LlmModelFetcherService>();

// ── Node Realization services (spec 007) ───────────────────────────────────────
// Schema-bound LLM output for turning plain-language nodes into executable config (Article VII), over the
// provider-neutral IChatClient (structured output via ChatResponseFormat.ForJsonSchema).
builder.Services.AddSingleton<IStructuredCompletionService>(sp =>
    new DBAIAzure.Processes.Ai.ChatClientStructuredCompletionService(
        sp.GetRequiredService<Microsoft.Extensions.AI.IChatClient>()));
// Scoped to mirror WorkflowBuilderService — one realization/readiness instance per session.
builder.Services.AddScoped<IWorkflowRealizationService, WorkflowRealizationService>();
builder.Services.AddScoped<IWorkflowReadinessService, WorkflowReadinessService>();

// WorkflowExecutionOrchestrator: singleton that owns all visual-workflow run lifecycles on MAF Workflows.
builder.Services.AddSingleton<WorkflowExecutionOrchestrator>(sp =>
{
    var configRepo       = sp.GetRequiredService<IConnectorConfigRepository>();
    var chatClient       = sp.GetRequiredService<Microsoft.Extensions.AI.IChatClient>();
    var runRepo          = sp.GetRequiredService<IWorkflowRunRepository>();
    var approvalNotifier = sp.GetRequiredService<IWorkflowApprovalNotifier>();
    var observers        = sp.GetServices<IWorkflowObserver>();
    var checkpointManager = sp.GetService<Microsoft.Agents.AI.Workflows.CheckpointManager>();

    // T048: broadcast run status changes to SignalR so non-Blazor clients (e.g., external dashboards)
    // receive real-time updates without polling. The Blazor Review Queue uses the in-process
    // RunUpdated event; this callback serves cross-process / cross-server consumers.
    var hubContext = sp.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<WorkflowRunHub>>();
    Func<string, string, Task> broadcastUpdate = (runId, statusText) =>
        hubContext.Clients.All.SendAsync("RunStatusChanged", runId, statusText);

    return new WorkflowExecutionOrchestrator(
        chatClient, runRepo, approvalNotifier, observers, broadcastUpdate, configRepo, checkpointManager);
});

// Expose the same singleton through the interface (UI/Review Queue consume the interface; the boot-time
// rehydration service needs the concrete type for the checkpoint-resume overload — spec-019 T032).
builder.Services.AddSingleton<IWorkflowExecutionOrchestrator>(
    sp => sp.GetRequiredService<WorkflowExecutionOrchestrator>());

var app = builder.Build();

// ── Ensure database is created on startup ─────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PipelineDbContext>();
    await db.Database.EnsureCreatedAsync();

    // Add ConnectorConfigs table for databases that existed before this feature was added.
    // CREATE TABLE IF NOT EXISTS is idempotent — safe to run on every startup.
    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS ConnectorConfigs (
            Id                   INTEGER PRIMARY KEY AUTOINCREMENT,
            ConnectorType        TEXT    NOT NULL,
            ConfigJson           TEXT,
            EncryptedSecretsJson TEXT,
            IsConfigured         INTEGER NOT NULL DEFAULT 0,
            LastUpdatedAt        TEXT    NOT NULL DEFAULT '0001-01-01T00:00:00+00:00',
            LastTestResult       TEXT,
            LastTestMessage      TEXT,
            LastTestedAt         TEXT
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_ConnectorConfigs_ConnectorType
            ON ConnectorConfigs (ConnectorType);
        """);

    // spec-020 (FR-015): one-time, idempotent migration of an existing Azure DevOps connector onto the
    // generic Work Tracking System connector. Runs before the app serves traffic and before any adapter/
    // BoardsClient use, so an existing ADO deployment keeps working with zero reconfiguration.
    await DBAIAzure.Storage.Migrations.WorkTrackerConnectorMigration.MigrateAsync(db);

    // Add WorkflowDefinitions table for databases created before the Visual Workflow Builder was added.
    // CREATE TABLE IF NOT EXISTS is idempotent — safe to run on every startup.
    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS WorkflowDefinitions (
            Id             TEXT    NOT NULL PRIMARY KEY,
            Name           TEXT    NOT NULL,
            OwnerId        TEXT    NOT NULL,
            NodesJson      TEXT    NOT NULL DEFAULT '[]',
            EdgesJson      TEXT    NOT NULL DEFAULT '[]',
            SettingsJson   TEXT    NOT NULL DEFAULT '{{}}',
            ChatHistoryJson TEXT   NOT NULL DEFAULT '[]',
            GeneratedCode  TEXT,
            ThumbnailSvg   TEXT,
            CreatedAt      TEXT    NOT NULL DEFAULT '0001-01-01T00:00:00+00:00',
            LastModifiedAt TEXT    NOT NULL DEFAULT '0001-01-01T00:00:00+00:00'
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_WorkflowDefinitions_OwnerId_Name
            ON WorkflowDefinitions (OwnerId, Name);
        CREATE INDEX IF NOT EXISTS IX_WorkflowDefinitions_OwnerId
            ON WorkflowDefinitions (OwnerId);
        """);

    // DoR workflow instances (spec-021) — lifecycle + SLA record for existing databases. The filtered unique
    // index enforces idempotency (one active instance per ticket); State ordinal 9 is DorState.Done.
    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS DorWorkflowInstances (
            RunId               TEXT    NOT NULL PRIMARY KEY,
            TicketKey           TEXT    NOT NULL,
            State               INTEGER NOT NULL DEFAULT 0,
            OutstandingGapsJson TEXT    NOT NULL DEFAULT '[]',
            PrimaryIterations   INTEGER NOT NULL DEFAULT 0,
            EscalationIterations INTEGER NOT NULL DEFAULT 0,
            SlaClockStartedAt   TEXT,
            SlaDeadlineAt       TEXT,
            SlaTier             INTEGER NOT NULL DEFAULT 0,
            ActiveChannelId     TEXT    NOT NULL DEFAULT '',
            ThreadRef           TEXT    NOT NULL DEFAULT '',
            LastSeenReplyRef    TEXT,
            IsDryRun            INTEGER NOT NULL DEFAULT 0,
            Outcome             INTEGER,
            StartedAt           TEXT    NOT NULL DEFAULT '0001-01-01T00:00:00+00:00',
            UpdatedAt           TEXT    NOT NULL DEFAULT '0001-01-01T00:00:00+00:00',
            CompletedAt         TEXT,
            FailureReason       TEXT
        );
        CREATE INDEX IF NOT EXISTS IX_DorWorkflowInstances_SlaDeadlineAt
            ON DorWorkflowInstances (SlaDeadlineAt);
        CREATE UNIQUE INDEX IF NOT EXISTS UX_DorWorkflowInstances_ActiveTicket
            ON DorWorkflowInstances (TicketKey) WHERE State <> 9;
        """);

    // Workflow builder run records and execution event audit log (FR-18, FR-21, US1, US4).
    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS WorkflowBuilderRuns (
            RunId         TEXT    NOT NULL PRIMARY KEY,
            WorkflowId    TEXT    NOT NULL,
            WorkflowName  TEXT    NOT NULL,
            Status        INTEGER NOT NULL DEFAULT 0,
            TriggeredBy   TEXT    NOT NULL DEFAULT '',
            StartedAt     TEXT    NOT NULL,
            SuspendedAt   TEXT,
            ResumedAt     TEXT,
            CompletedAt   TEXT,
            FailureReason TEXT
        );
        CREATE INDEX IF NOT EXISTS IX_WorkflowBuilderRuns_WorkflowId
            ON WorkflowBuilderRuns (WorkflowId);
        CREATE INDEX IF NOT EXISTS IX_WorkflowBuilderRuns_Status
            ON WorkflowBuilderRuns (Status);
        """);

    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS WorkflowExecutionEvents (
            EventId         TEXT    NOT NULL PRIMARY KEY,
            RunId           TEXT    NOT NULL,
            NodeId          TEXT,
            NodeLabel       TEXT,
            EventType       INTEGER NOT NULL,
            OccurredAt      TEXT    NOT NULL,
            DurationMs      INTEGER,
            Outcome         TEXT,
            LlmModelName    TEXT,
            LlmInputTokens  INTEGER,
            LlmOutputTokens INTEGER,
            FOREIGN KEY (RunId) REFERENCES WorkflowBuilderRuns(RunId) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS IX_WorkflowExecutionEvents_RunId
            ON WorkflowExecutionEvents (RunId);
        CREATE INDEX IF NOT EXISTS IX_WorkflowExecutionEvents_OccurredAt
            ON WorkflowExecutionEvents (OccurredAt);
        CREATE INDEX IF NOT EXISTS IX_WorkflowExecutionEvents_RunId_OccurredAt
            ON WorkflowExecutionEvents (RunId, OccurredAt);
        """);

    // Registered repo-apps, their monitoring heartbeats, and close-the-loop dedup signatures
    // (feature 013). CREATE TABLE IF NOT EXISTS is idempotent — safe on every startup.
    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS MonitoredApps (
            AppId               TEXT    NOT NULL PRIMARY KEY,
            Name                TEXT    NOT NULL,
            OwnerId             TEXT    NOT NULL,
            RepoLocalPath       TEXT    NOT NULL,
            Branch              TEXT,
            BuildCommand        TEXT,
            RunCommand          TEXT    NOT NULL,
            Status              INTEGER NOT NULL DEFAULT 0,
            LastBuildResultJson TEXT,
            LastRunResultJson   TEXT,
            LinkedWorkflowId    TEXT,
            LastBuiltAt         TEXT,
            LastRunAt           TEXT,
            CreatedAt           TEXT    NOT NULL DEFAULT '0001-01-01T00:00:00+00:00',
            UpdatedAt           TEXT    NOT NULL DEFAULT '0001-01-01T00:00:00+00:00'
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_MonitoredApps_OwnerId_Name
            ON MonitoredApps (OwnerId, Name);
        CREATE INDEX IF NOT EXISTS IX_MonitoredApps_OwnerId
            ON MonitoredApps (OwnerId);
        """);

    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS AppMonitoringHeartbeats (
            AppId       TEXT    NOT NULL PRIMARY KEY,
            LastCycleAt TEXT    NOT NULL,
            LastCycleOk INTEGER NOT NULL DEFAULT 0,
            LastError   TEXT,
            CycleCount  INTEGER NOT NULL DEFAULT 0
        );
        """);

    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS AppRaisedIssues (
            Signature     TEXT NOT NULL PRIMARY KEY,
            AppId         TEXT NOT NULL,
            WorkflowRunId TEXT,
            CreatedAt     TEXT NOT NULL DEFAULT '0001-01-01T00:00:00+00:00'
        );
        CREATE INDEX IF NOT EXISTS IX_AppRaisedIssues_AppId
            ON AppRaisedIssues (AppId);
        """);

    // ── Seed the demo's back-office connectors from environment configuration (012 US5) ───
    // Runs after the ConnectorConfigs table exists and before the app serves traffic. Never seeds
    // the LLM connector. On the demo's ephemeral container this re-populates connectors on every
    // cold start (research Decision 4); locally it is a no-op when no seed env vars are present.
    var demoSeeder = scope.ServiceProvider.GetRequiredService<DemoConnectorSeeder>();
    await demoSeeder.SeedAsync();

    // Remove the pre-spec-021 "Support Request Flow" example so the DoR workflow is the only starter and the
    // builder never resumes the removed demo (spec-021).
    var legacyExamplePurger = scope.ServiceProvider.GetRequiredService<LegacyExampleWorkflowPurger>();
    await legacyExamplePurger.PurgeAsync();
}

// ── Field provisioning auto-run on startup (spec-009 T034; spec-018 T025) ──────
// Fire-and-forget so startup is not blocked. Routes through the ACTIVE work-tracker adapter — ADO runs
// its preflight (incl. inherited-process handling), Jira runs its field/context provisioner — so the
// telemetry/cost fields are usable on whichever tracker is configured. Scoped to avoid a captive root provider.
app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(async () =>
    {
        await using var scope = app.Services.CreateAsyncScope();
        var adapter = scope.ServiceProvider
            .GetRequiredService<DBAIAzure.Core.Interfaces.IWorkTrackerAdapterProvider>().GetAdapter();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<DBAIAzure.Web.Integrations.AzureDevOps.AdoTelemetryPreflightService>>();
        try
        {
            var config = await DBAIAzure.Web.Integrations.AzureDevOps.AdoTelemetryPreflightService
                .LoadDefaultConfigAsync(CancellationToken.None);
            var result = await adapter.ProvisionFieldsAsync(config, CancellationToken.None);
            if (result.IsSuccess)
                logger.LogInformation("Field provisioning on startup: tracker={Tracker} mode={Mode} ready={Ready}",
                    adapter.TrackerKey, result.Mode, result.FieldsReady.Count);
            else
                logger.LogWarning("Field provisioning incomplete on startup: tracker={Tracker} mode={Mode} failed={Failed}",
                    adapter.TrackerKey, result.Mode, result.FieldsFailed.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Field provisioning threw unexpectedly on startup — pipeline continues.");
        }
    });
});

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.UseStaticFiles();
app.UseRouting();

app.MapControllers();
app.MapRazorPages();
app.MapBlazorHub();
app.MapHub<WorkflowRunHub>("/hubs/workflow-run");
app.MapFallbackToPage("/_Host");

app.Run();
