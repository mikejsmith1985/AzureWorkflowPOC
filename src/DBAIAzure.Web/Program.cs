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
using Microsoft.SemanticKernel;
using Microsoft.AspNetCore.SignalR;
using Microsoft.SemanticKernel.ChatCompletion;

#pragma warning disable SKEXP0080

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

// ── Pipeline orchestrator ──────────────────────────────────────────────────────
builder.Services.AddSingleton<PipelineOrchestrator>(sp =>
{
    var configRepo = sp.GetRequiredService<IConnectorConfigRepository>();
    var usageReporter = sp.GetRequiredService<DBAIAzure.Core.Interfaces.ILlmUsageReporter>();

    // Kernel factory: resolves LLM credentials from DB at each run start (hot-reload, FR-014).
    // Runs on a thread-pool thread (inside Task.Run), so synchronous .GetResult() is safe.
    Func<IProgressReporter, Kernel> kernelFactory = reporter =>
    {
        var effectiveKey   = anthropicKey;
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

        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.Services.AddSingleton<IChatCompletionService>(
            new AnthropicChatCompletionService(effectiveKey, effectiveModel, usageReporter));
        kernelBuilder.Services.AddSingleton<IProgressReporter>(reporter);
        return kernelBuilder.Build();
    };

    var repo          = sp.GetRequiredService<IRunRepository>();
    var notifier      = sp.GetService<IHitlNotifier>();
    var healthChecker = sp.GetService<IConnectorHealthChecker>();

    // spec-019 T022/T032: hand the orchestrator the MAF model client, runtime flag, and checkpoint manager.
    var chatClient = sp.GetService<Microsoft.Extensions.AI.IChatClient>();
    var runOnMaf   = builder.Configuration.GetValue<bool>("Maf:Enabled");
    var checkpointManager = sp.GetService<Microsoft.Agents.AI.Workflows.CheckpointManager>();

    return new PipelineOrchestrator(
        kernelFactory, repo, notifier, portalBaseUrl, healthChecker, chatClient, runOnMaf, checkpointManager);
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

// ── Work-tracker adapter (spec-018): tracker-neutral seam + ADO implementation ───────────────
// Additive — the adapter wraps the existing ADO client/preflight; the pipeline is not yet rewired
// onto it (that is a later increment), so live ADO behaviour is unchanged. Scoped because the ADO
// adapter depends on the scoped IAdoTelemetryPreflightService.
builder.Services.AddSingleton<DBAIAzure.Web.Integrations.AzureDevOps.AdoFieldReferenceResolver>();
builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.IWorkTrackerAdapter,
    DBAIAzure.Web.Integrations.AzureDevOps.AzureDevOpsWorkTrackerAdapter>();

// ── Jira work-tracker adapter (spec-018 increment 3) — registered alongside ADO; the provider selects
// the active one by WorkTracker:Active. Only exercised when Jira is the configured tracker. ────────────
builder.Services.AddSingleton(sp =>
{
    var jiraOptions = new DBAIAzure.Web.Integrations.Jira.JiraOptions();
    sp.GetRequiredService<IConfiguration>().GetSection("WorkTracker:Jira").Bind(jiraOptions);
    return jiraOptions;
});
builder.Services.AddHttpClient("Jira", (sp, client) =>
{
    var jiraOptions = sp.GetRequiredService<DBAIAzure.Web.Integrations.Jira.JiraOptions>();
    if (!string.IsNullOrWhiteSpace(jiraOptions.SiteUrl))
        client.BaseAddress = new Uri(jiraOptions.SiteUrl);
    var basicToken = Convert.ToBase64String(
        System.Text.Encoding.ASCII.GetBytes($"{jiraOptions.Email}:{jiraOptions.ApiToken}"));
    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", basicToken);
});
builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.IWorkTrackerAdapter>(sp =>
    new DBAIAzure.Web.Integrations.Jira.JiraWorkTrackerAdapter(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("Jira"),
        sp.GetRequiredService<DBAIAzure.Web.Integrations.Jira.JiraOptions>(),
        sp.GetRequiredService<DBAIAzure.Core.Interfaces.IBindingWorkItemMap>(),
        sp.GetRequiredService<ILogger<DBAIAzure.Web.Integrations.Jira.JiraWorkTrackerAdapter>>()));

builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.IWorkTrackerAdapterProvider,
    DBAIAzure.Web.Services.WorkTrackerAdapterProvider>();

builder.Services.AddSingleton<PhaseHandlerOrchestrator>(sp =>
{
    var configRepo     = sp.GetRequiredService<IConnectorConfigRepository>();
    var artifactReader = sp.GetRequiredService<IArtifactReader>();
    var boardsClient   = sp.GetRequiredService<IBoardsClient>();
    var phaseRepo      = sp.GetRequiredService<IPhaseRunRepository>();
    var telemetryWriteBack = sp.GetRequiredService<DBAIAzure.Core.Interfaces.ITelemetryWriteBack>();
    var phaseUsageReporter = sp.GetRequiredService<DBAIAzure.Core.Interfaces.ILlmUsageReporter>();
    var bindingKeyMinter   = sp.GetRequiredService<DBAIAzure.Core.Interfaces.IBindingKeyMinter>();
    var bindingWorkItemMap = sp.GetRequiredService<DBAIAzure.Core.Interfaces.IBindingWorkItemMap>();
    var costLedger         = sp.GetRequiredService<DBAIAzure.Core.Interfaces.ICostLedger>();
    var costProjection     = sp.GetRequiredService<DBAIAzure.Core.Interfaces.ICostProjection>();
    var runTelemetrySource = sp.GetRequiredService<DBAIAzure.Core.Interfaces.IRunTelemetrySource>();
    var workTrackerAdapter = sp.GetRequiredService<DBAIAzure.Core.Interfaces.IWorkTrackerAdapterProvider>().GetAdapter();

    Func<IPhaseProgressSink, Kernel> kernelFactory = sink =>
    {
        var effectiveKey   = anthropicKey;
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
        catch { }

        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.Services.AddSingleton<IStructuredCompletionService>(
            new AnthropicChatCompletionService(effectiveKey, effectiveModel, phaseUsageReporter));
        kernelBuilder.Services.AddSingleton(artifactReader);
        kernelBuilder.Services.AddSingleton(boardsClient);
        kernelBuilder.Services.AddSingleton(phaseRepo);
        kernelBuilder.Services.AddSingleton(sink);
        kernelBuilder.Services.AddSingleton(telemetryWriteBack);
        kernelBuilder.Services.AddSingleton(bindingKeyMinter);
        kernelBuilder.Services.AddSingleton(bindingWorkItemMap);
        kernelBuilder.Services.AddSingleton(costLedger);
        kernelBuilder.Services.AddSingleton(costProjection);
        kernelBuilder.Services.AddSingleton(runTelemetrySource);
        kernelBuilder.Services.AddSingleton(workTrackerAdapter);
        return kernelBuilder.Build();
    };

    var notifier      = sp.GetService<IPhaseApprovalNotifier>();
    var healthChecker = sp.GetService<IConnectorHealthChecker>();

    // spec-019 T022/T032: hand the orchestrator the MAF model client + executor deps + flag + checkpoints.
    var chatClient = sp.GetService<Microsoft.Extensions.AI.IChatClient>();
    var runOnMaf   = builder.Configuration.GetValue<bool>("Maf:Enabled");
    var checkpointManager = sp.GetService<Microsoft.Agents.AI.Workflows.CheckpointManager>();

    return new PhaseHandlerOrchestrator(
        kernelFactory, phaseRepo, notifier, portalBaseUrl, healthChecker,
        chatClient, artifactReader, bindingKeyMinter, runOnMaf, checkpointManager);
});

// ── Connector health checker + per-connector testers (T020) ───────────────────
builder.Services.AddSingleton<ServiceNowClient>();
builder.Services.AddSingleton<AdoConnectorTester>();
builder.Services.AddSingleton<LlmConnectorTester>();
builder.Services.AddSingleton<MessagingConnectorTester>();
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
builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.IChatClientProvider,
    DBAIAzure.Connectors.Ai.AnthropicChatClientProvider>();
builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.IChatClientProviderRegistry>(sp =>
    new DBAIAzure.Connectors.Ai.ChatClientProviderRegistry(
        sp.GetServices<DBAIAzure.Core.Interfaces.IChatClientProvider>()));
builder.Services.AddSingleton<Microsoft.Extensions.AI.IChatClient>(sp =>
{
    var registry = sp.GetRequiredService<DBAIAzure.Core.Interfaces.IChatClientProviderRegistry>();
    var configRepo = sp.GetRequiredService<IConnectorConfigRepository>();
    var usageReporter = sp.GetRequiredService<DBAIAzure.Core.Interfaces.ILlmUsageReporter>();
    var costLogger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<DBAIAzure.Web.Services.Ai.CostCapturingChatClient>();

    // Re-resolve the active provider config from the DB LLM connector on each call (config fallback),
    // mirroring the per-run kernel factory so the single visitor-supplied key powers every model call.
    DBAIAzure.Core.Models.Ai.AiProviderConfig ResolveActiveConfig()
    {
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
    return new DBAIAzure.Web.Services.Ai.CostCapturingChatClient(hotReload, usageReporter, costLogger);
});

// ── Durable MAF checkpointing (spec-019 T030/T032) ────────────────────────────
// The EF-backed checkpoint store + JSON manager let MAF runs paused at a HITL gate resume after a
// restart. Passed to the orchestrators below so their runs are checkpointed when the MAF flag is on.
builder.Services.AddSingleton<DBAIAzure.Storage.Checkpointing.EfCheckpointStore>();
builder.Services.AddSingleton<Microsoft.Agents.AI.Workflows.CheckpointManager>(sp =>
    Microsoft.Agents.AI.Workflows.CheckpointManager.CreateJson(
        sp.GetRequiredService<DBAIAzure.Storage.Checkpointing.EfCheckpointStore>(),
        new System.Text.Json.JsonSerializerOptions()));

// ── SK kernel filters (FR-21.2 token tracing, Article IX prompt hashing) ──────
builder.Services.AddSingleton<WorkflowFunctionInvocationFilter>();
builder.Services.AddSingleton<WorkflowPromptRenderFilter>();

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

// ── Application Insights (FR-21, US4) ─────────────────────────────────────────
builder.Services.AddApplicationInsightsTelemetry(builder.Configuration);

// ── Workflow structural validator (spec 004) ───────────────────────────────────
builder.Services.AddSingleton<IWorkflowValidator, WorkflowValidator>();

// ── Design-time LLM services (Workflow Builder assistant + Node Realization) ───────────
// These resolve the LLM key+model from the LLM connector's DB row on each call (config fallback),
// so the single key a visitor enters in the UI powers the builder assistant and node realization
// without an app restart — matching the per-run execution factories (research Decision 7;
// FR-003/FR-004/SC-006). One shared instance backs both IChatCompletionService and
// IStructuredCompletionService.
builder.Services.AddSingleton<HotReloadAnthropicService>(sp =>
    new HotReloadAnthropicService(sp.GetRequiredService<IConnectorConfigRepository>(), anthropicKey, anthropicModel));
builder.Services.AddSingleton<IChatCompletionService>(sp => sp.GetRequiredService<HotReloadAnthropicService>());

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
// Schema-bound LLM output for turning plain-language nodes into executable config (Article VII).
// Resolves to the same hot-reloading service as the builder assistant, so node realization uses the
// visitor-entered key from the DB rather than a key captured at startup (research Decision 7).
builder.Services.AddSingleton<IStructuredCompletionService>(sp => sp.GetRequiredService<HotReloadAnthropicService>());
// Scoped to mirror WorkflowBuilderService — one realization/readiness instance per session.
builder.Services.AddScoped<IWorkflowRealizationService, WorkflowRealizationService>();
builder.Services.AddScoped<IWorkflowReadinessService, WorkflowReadinessService>();

// WorkflowExecutionOrchestrator: singleton that owns all visual-workflow run lifecycles.
// Accepts a Func<Kernel> so it can re-read LLM credentials from the DB on each run (hot-reload).
builder.Services.AddSingleton<IWorkflowExecutionOrchestrator>(sp =>
{
    var configRepo = sp.GetRequiredService<IConnectorConfigRepository>();

    Func<Kernel> kernelFactory = () =>
    {
        var effectiveKey   = anthropicKey;
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
        catch { }

        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.Services.AddSingleton<IChatCompletionService>(
            new AnthropicChatCompletionService(effectiveKey, effectiveModel));

        var kernel = kernelBuilder.Build();

        // Register the LLM token-tracking filter so token counts appear in Run History.
        var invocationFilter = sp.GetRequiredService<WorkflowFunctionInvocationFilter>();
        kernel.FunctionInvocationFilters.Add(invocationFilter);

        var promptFilter = sp.GetRequiredService<WorkflowPromptRenderFilter>();
        kernel.PromptRenderFilters.Add(promptFilter);

        return kernel;
    };

    var runRepo          = sp.GetRequiredService<IWorkflowRunRepository>();
    var approvalNotifier = sp.GetRequiredService<IWorkflowApprovalNotifier>();
    var observers        = sp.GetServices<IWorkflowObserver>();

    // T048: broadcast run status changes to SignalR so non-Blazor clients (e.g., external dashboards)
    // receive real-time updates without polling. The Blazor Review Queue uses the in-process
    // RunUpdated event; this callback serves cross-process / cross-server consumers.
    var hubContext = sp.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<WorkflowRunHub>>();
    Func<string, string, Task> broadcastUpdate = (runId, statusText) =>
        hubContext.Clients.All.SendAsync("RunStatusChanged", runId, statusText);

    return new WorkflowExecutionOrchestrator(kernelFactory, runRepo, approvalNotifier, observers, broadcastUpdate);
});

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
