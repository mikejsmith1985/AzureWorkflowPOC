using System.Text.Json;
using DBAIAzure.Connectors;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Services;
using DBAIAzure.Core.Validation;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Repositories;
using DBAIAzure.Web.Integrations.AzureDevOps;
using DBAIAzure.Web.Integrations.SpecKit;
using DBAIAzure.Web.Integrations.Teams;
using DBAIAzure.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

#pragma warning disable SKEXP0080

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ──────────────────────────────────────────────────────────────
var anthropicKey   = builder.Configuration["Anthropic:ApiKey"]   ?? string.Empty;
var anthropicModel = builder.Configuration["Anthropic:Model"]    ?? "claude-sonnet-4-6";
var portalBaseUrl  = builder.Configuration["Portal:BaseUrl"]     ?? "http://localhost:5000";
var teamsWebhook   = builder.Configuration["Teams:PowerAutomateUrl"] ?? string.Empty;
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
builder.Services.AddDataProtection();
builder.Services.AddSingleton<ISecretProtector, DataProtectorAdapter>();

// ── Connector config repository (T013) ────────────────────────────────────────
builder.Services.AddSingleton<IConnectorConfigRepository, SqliteConnectorConfigRepository>();

// ── Workflow persistence ────────────────────────────────────────────────────
builder.Services.AddSingleton<IWorkflowRepository, SqliteWorkflowRepository>();

// ── ServiceNow outbound HTTP client — 35 s timeout, base URL set per-request (T002) ─
builder.Services.AddHttpClient(nameof(ServiceNowClient), client =>
{
    client.Timeout = TimeSpan.FromSeconds(35);
});

// ── Teams HITL notifier ────────────────────────────────────────────────────────
builder.Services.AddHttpClient(nameof(TeamsHitlNotifier), client =>
{
    if (!string.IsNullOrWhiteSpace(teamsWebhook))
        client.BaseAddress = new Uri(teamsWebhook);
});
// IConnectorConfigRepository is registered above, so it is injectable as optional param (FR-014).
builder.Services.AddSingleton<IHitlNotifier>(sp =>
    new TeamsHitlNotifier(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<ILogger<TeamsHitlNotifier>>(),
        sp.GetService<IConnectorConfigRepository>()));

// ── Pipeline orchestrator ──────────────────────────────────────────────────────
builder.Services.AddSingleton<PipelineOrchestrator>(sp =>
{
    var configRepo = sp.GetRequiredService<IConnectorConfigRepository>();

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
            new AnthropicChatCompletionService(effectiveKey, effectiveModel));
        kernelBuilder.Services.AddSingleton<IProgressReporter>(reporter);
        return kernelBuilder.Build();
    };

    var repo          = sp.GetRequiredService<IRunRepository>();
    var notifier      = sp.GetService<IHitlNotifier>();
    var healthChecker = sp.GetService<IConnectorHealthChecker>();

    return new PipelineOrchestrator(kernelFactory, repo, notifier, portalBaseUrl, healthChecker);
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

builder.Services.AddSingleton<PhaseHandlerOrchestrator>(sp =>
{
    var configRepo     = sp.GetRequiredService<IConnectorConfigRepository>();
    var artifactReader = sp.GetRequiredService<IArtifactReader>();
    var boardsClient   = sp.GetRequiredService<IBoardsClient>();
    var phaseRepo      = sp.GetRequiredService<IPhaseRunRepository>();

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
            new AnthropicChatCompletionService(effectiveKey, effectiveModel));
        kernelBuilder.Services.AddSingleton(artifactReader);
        kernelBuilder.Services.AddSingleton(boardsClient);
        kernelBuilder.Services.AddSingleton(phaseRepo);
        kernelBuilder.Services.AddSingleton(sink);
        return kernelBuilder.Build();
    };

    var notifier      = sp.GetService<IPhaseApprovalNotifier>();
    var healthChecker = sp.GetService<IConnectorHealthChecker>();

    return new PhaseHandlerOrchestrator(kernelFactory, phaseRepo, notifier, portalBaseUrl, healthChecker);
});

// ── Connector health checker + per-connector testers (T020) ───────────────────
builder.Services.AddSingleton<ServiceNowClient>();
builder.Services.AddSingleton<AdoConnectorTester>();
builder.Services.AddSingleton<LlmConnectorTester>();
builder.Services.AddSingleton<TeamsConnectorTester>();
builder.Services.AddSingleton<IConnectorHealthChecker, ConnectorHealthChecker>();

// ── Workflow structural validator (spec 004) ───────────────────────────────────
builder.Services.AddSingleton<IWorkflowValidator, WorkflowValidator>();

// ── Visual Workflow Builder services (T046–T049) ───────────────────────────────
// A singleton IChatCompletionService for the Visual Workflow Builder (separate from the
// per-run kernel factory used by the pipeline orchestrator).
builder.Services.AddSingleton<IChatCompletionService>(
    new AnthropicChatCompletionService(anthropicKey, anthropicModel));

builder.Services.AddSingleton<WorkflowTopologySerializer>();
builder.Services.AddSingleton<ILlmAvailabilityMonitor, LlmAvailabilityMonitor>();
builder.Services.AddSingleton<IWorkflowCodeGenerator, WorkflowCodeGenerator>();
builder.Services.AddSingleton<WorkflowDesignSkillService>();
builder.Services.AddSingleton<IWorkflowThumbnailGenerator, WorkflowThumbnailGenerator>();
builder.Services.AddSingleton<IWorkflowCodeDiffService, WorkflowCodeDiffService>();
// WorkflowBuilderService is scoped (one instance per session / per page).
builder.Services.AddScoped<WorkflowBuilderService>();

// ── Node Realization services (spec 007) ───────────────────────────────────────
// Schema-bound LLM output for turning plain-language nodes into executable config (Article VII).
// AnthropicChatCompletionService implements IStructuredCompletionService; registered at the app root
// (the existing IStructuredCompletionService lives only inside the per-run pipeline kernel).
builder.Services.AddSingleton<IStructuredCompletionService>(
    new AnthropicChatCompletionService(anthropicKey, anthropicModel));
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
        return kernelBuilder.Build();
    };

    return new WorkflowExecutionOrchestrator(kernelFactory);
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
}

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.UseStaticFiles();
app.UseRouting();

app.MapControllers();
app.MapRazorPages();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
