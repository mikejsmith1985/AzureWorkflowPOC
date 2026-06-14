using DBAIAzure.Connectors;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Repositories;
using DBAIAzure.Web.Integrations.Teams;
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
