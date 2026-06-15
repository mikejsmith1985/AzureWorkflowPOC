using DBAIAzure.Connectors;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
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
    throw new InvalidOperationException(
        "Anthropic:ApiKey is required. Add it to appsettings.Development.json (gitignored).");

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

// ── Teams HITL notifier ────────────────────────────────────────────────────────
builder.Services.AddHttpClient(nameof(TeamsHitlNotifier), client =>
{
    if (!string.IsNullOrWhiteSpace(teamsWebhook))
        client.BaseAddress = new Uri(teamsWebhook);
});
builder.Services.AddSingleton<IHitlNotifier>(sp =>
    new TeamsHitlNotifier(sp.GetRequiredService<IHttpClientFactory>(),
                          sp.GetRequiredService<ILogger<TeamsHitlNotifier>>()));

// ── Pipeline orchestrator ──────────────────────────────────────────────────────
builder.Services.AddSingleton<PipelineOrchestrator>(sp =>
{
    Func<IProgressReporter, Kernel> kernelFactory = reporter =>
    {
        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.Services.AddSingleton<IChatCompletionService>(
            new AnthropicChatCompletionService(anthropicKey, anthropicModel));
        kernelBuilder.Services.AddSingleton<IProgressReporter>(reporter);
        return kernelBuilder.Build();
    };

    // Repository and notifier resolved here (singleton scope is fine — both are thread-safe)
    var repo     = sp.GetRequiredService<IRunRepository>();
    var notifier = sp.GetService<IHitlNotifier>();

    return new PipelineOrchestrator(kernelFactory, repo, notifier, portalBaseUrl);
});

// ── Spec Kit phase handler (parallel track — does not touch the ticket pipeline) ───────────────
builder.Services.Configure<SpecKitOptions>(builder.Configuration.GetSection(SpecKitOptions.SectionName));
builder.Services.Configure<AzureDevOpsOptions>(builder.Configuration.GetSection(AzureDevOpsOptions.SectionName));

// Phase-run persistence — same singleton-safe DbContextFactory pattern as the ticket repository.
builder.Services.AddSingleton<IPhaseRunRepository, SqlitePhaseRunRepository>();

// Seams resolved as app-level singletons; the kernel factory injects them per run.
builder.Services.AddSingleton<IArtifactReader, FileSystemArtifactReader>();
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
    // Each run gets a kernel wired with the structured chat service and the seam services; the
    // orchestrator supplies the per-run progress sink.
    var artifactReader = sp.GetRequiredService<IArtifactReader>();
    var boardsClient   = sp.GetRequiredService<IBoardsClient>();
    var phaseRepo      = sp.GetRequiredService<IPhaseRunRepository>();

    Func<IPhaseProgressSink, Kernel> kernelFactory = sink =>
    {
        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.Services.AddSingleton<IStructuredCompletionService>(
            new AnthropicChatCompletionService(anthropicKey, anthropicModel));
        kernelBuilder.Services.AddSingleton(artifactReader);
        kernelBuilder.Services.AddSingleton(boardsClient);
        // The create step resolves the phase-run repository for hierarchy linking + idempotent
        // upsert (FR-012 / FR-013); mirror how IBoardsClient is provided to the kernel.
        kernelBuilder.Services.AddSingleton(phaseRepo);
        kernelBuilder.Services.AddSingleton(sink);
        return kernelBuilder.Build();
    };
    var notifier  = sp.GetService<IPhaseApprovalNotifier>();

    return new PhaseHandlerOrchestrator(kernelFactory, phaseRepo, notifier, portalBaseUrl);
});

var app = builder.Build();

// ── Ensure database is created on startup ─────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PipelineDbContext>();
    await db.Database.EnsureCreatedAsync();
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
