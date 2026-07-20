// Integration test for SLA escalation + manual handoff (spec-021 US3/US4 / T055): a not-ready ticket with a
// breached SLA escalates to the escalation tier, and a second breach ends the run as a clean manual handoff that
// does not transition the ticket. Drives the real MAF graph with the real SLA sweeper + in-memory SQLite.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Core.Models.DorWorkflow.Config;
using DBAIAzure.Core.Models.WorkTracker;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Repositories;
using DBAIAzure.Web.Services.Dor;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.Dor.Integration;

public sealed class DorEscalationTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly SqliteConnection _keepAlive;
    private readonly DbContextOptions<PipelineDbContext> _options;
    private readonly EfDorWorkflowInstanceStore _store;

    public DorEscalationTests()
    {
        _keepAlive = new SqliteConnection("Data Source=:memory:");
        _keepAlive.Open();
        _options = new DbContextOptionsBuilder<PipelineDbContext>().UseSqlite(_keepAlive).Options;
        using (var seed = new PipelineDbContext(_options)) seed.Database.EnsureCreated();
        _store = new EfDorWorkflowInstanceStore(new SharedFactory(_options));
    }

    public void Dispose() => _keepAlive.Dispose();

    [Fact]
    public async Task PrimaryBreach_Escalates_ThenEscalationBreach_HandsOffManually()
    {
        var adapter = new RecordingAdapter();
        var messaging = new CountingMessageDelivery();
        var orchestrator = BuildOrchestrator(adapter, messaging);
        var sweeper = new DorSlaSweeperService(_store, orchestrator, NullLogger<DorSlaSweeperService>.Instance);

        var run = await orchestrator.StartAsync("SBRO-1");
        await run!.WaitSuspendedAsync().WaitAsync(Timeout);           // primary tier, awaiting a human

        // Primary SLA (0h) is already breached → the sweep escalates.
        Assert.Equal(1, await sweeper.RunOnceAsync());
        await run.WaitSuspendedAsync().WaitAsync(Timeout);            // escalation tier, awaiting

        var escalated = await LoadInstanceAsync("SBRO-1");
        Assert.Equal(DorState.Escalated, escalated.State);
        Assert.Equal(SlaTier.Escalation, escalated.SlaTier);
        Assert.True(messaging.Count >= 2);                            // primary gap + escalation summary

        // Escalation SLA (0h) is also breached → the sweep forces a manual exit.
        Assert.Equal(1, await sweeper.RunOnceAsync());
        await run.Completion.WaitAsync(Timeout);

        var final = await LoadInstanceAsync("SBRO-1");
        Assert.Equal(DorState.Done, final.State);
        Assert.Equal(DorOutcome.ManualRequired, final.Outcome);
        Assert.Empty(adapter.Transitions);                           // manual exit never transitions the ticket
        Assert.NotEmpty(adapter.Comments);                           // a manual-handoff comment was posted
    }

    // ── Wiring ──────────────────────────────────────────────────────────────────

    private DorWorkflowOrchestrator BuildOrchestrator(RecordingAdapter adapter, CountingMessageDelivery messaging) =>
        new(
            new FailReviewService(), new UnusedConversation(), adapter, new StubDocumentSource(),
            new StubConfigResolver(), messaging, _store, NullLogger<DorWorkflowOrchestrator>.Instance);

    private async Task<DorWorkflowInstance> LoadInstanceAsync(string ticketKey)
    {
        await using var db = new PipelineDbContext(_options);
        var e = await db.DorWorkflowInstances.AsNoTracking().FirstAsync(x => x.TicketKey == ticketKey);
        return new DorWorkflowInstance
        {
            RunId = e.RunId, TicketKey = e.TicketKey, State = (DorState)e.State,
            SlaTier = (SlaTier)e.SlaTier, Outcome = e.Outcome is { } o ? (DorOutcome)o : null,
        };
    }

    // ── Fakes ───────────────────────────────────────────────────────────────────

    private sealed class FailReviewService : IDorReviewService
    {
        public Task<DorReviewResult> ReviewAsync(IReadOnlyDictionary<string, string?> ticketFields, string dorDocument, DorAiConfig ai, CancellationToken ct = default) =>
            Task.FromResult(new DorReviewResult("FAIL",
                new[] { new CriterionResult("Acceptance Criteria", "FAIL", "missing") },
                new[] { "acceptance_criteria" }, new Dictionary<string, string>()));
    }

    private sealed class UnusedConversation : IDorConversationService
    {
        public Task<ReplyEvaluation> EvaluateReplyAsync(IReadOnlyList<string> outstandingGaps, string humanReply, int iteration, DorAiConfig ai, CancellationToken ct = default) =>
            throw new InvalidOperationException("No reply is submitted in the escalation test.");
    }

    private sealed class StubDocumentSource : IDorDocumentSource
    {
        public Task<DorDocument> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult(new DorDocument("DOR", "v1", DateTimeOffset.UtcNow, "inline"));
    }

    private sealed class StubConfigResolver : IDorConfigResolver
    {
        public Task<DorWorkflowConfig> ResolveActiveAsync(CancellationToken ct = default) =>
            Task.FromResult(new DorWorkflowConfig
            {
                IsConfigured = true,
                Jira = new DorJiraConfig { ProjectKeys = new[] { "SBRO" }, WatchFields = new[] { "summary" }, ReadyTransitionId = "31", ManualLabel = "dor-manual-required" },
                // SLA of 0 hours (wall-clock) → the deadline is immediate, so both tiers breach right away.
                Sla = new DorSlaConfig { ClockType = "wall_clock", PrimarySlaHours = 0, EscalationSlaHours = 0 },
                Comms = new DorCommsConfig
                {
                    Primary = new DorChannelConfig { ChannelId = "#dor", MaxIterations = 3 },
                    Escalation = new DorChannelConfig { ChannelId = "#esc", MaxIterations = 2 },
                },
                Run = new DorRunConfig { DryRun = false },
            });
        public Task<DorWorkflowSecrets> ResolveSecretsAsync(CancellationToken ct = default) =>
            Task.FromResult(new DorWorkflowSecrets(null, null, null, null));
    }

    private sealed class CountingMessageDelivery : IMessageDelivery
    {
        public int Count;
        public Task<MessageDeliveryResult> SendAsync(string message, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            return Task.FromResult(new MessageDeliveryResult(true, MessagingPlatform.Slack, DeliveryPath.Mcp, "ok"));
        }
        public Task<ConnectorTestResult> TestConnectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectorTestResult(ConnectorType.Messaging, true, "ok", DateTimeOffset.UtcNow));
    }

    private sealed class RecordingAdapter : IWorkTrackerAdapter
    {
        public List<string> Transitions { get; } = [];
        public List<string> Comments { get; } = [];
        public string TrackerKey => "Fake";
        public Task<WorkItemFields> ReadWorkItemAsync(WorkItemRef item, IReadOnlyCollection<string> watchFields, CancellationToken ct = default) =>
            Task.FromResult(new WorkItemFields(item.Value, $"https://x/browse/{item.Value}", new Dictionary<string, string?> { ["summary"] = "x" }));
        public Task<string> TransitionAsync(WorkItemRef item, string transitionId, CancellationToken ct = default) { Transitions.Add(transitionId); return Task.FromResult(transitionId); }
        public Task AppendCommentAsync(WorkItemRef item, string comment, CancellationToken ct = default) { Comments.Add(comment); return Task.CompletedTask; }
        public Task SetFieldsAsync(WorkItemRef item, IReadOnlyDictionary<string, object?> logicalFields, CancellationToken ct = default) => Task.CompletedTask;
        public Task<CreatedWorkItemRef> CreateWorkItemAsync(WorkItemType type, string title, string description, WorkItemRef? parent, CancellationToken ct = default) =>
            Task.FromResult(new CreatedWorkItemRef { WorkItemId = new WorkItemRef("X-1"), WorkItemType = "Task", Url = "", WasUpdated = false });
        public Task<CreatedWorkItemRef> UpsertWorkItemAsync(WorkItemRef item, string title, string description, string appendComment, CancellationToken ct = default) =>
            Task.FromResult(new CreatedWorkItemRef { WorkItemId = item, WorkItemType = "", Url = "", WasUpdated = true });
        public Task<WorkItemRef?> ResolveByBindingKeyAsync(string bindingKey, CancellationToken ct = default) => Task.FromResult<WorkItemRef?>(null);
        public Task<ProvisioningResult> ProvisionFieldsAsync(AdoTelemetryFieldConfig fieldConfig, CancellationToken ct = default) => Task.FromResult(new ProvisioningResult { IsSuccess = true, Mode = "Fake" });
        public RollupCapability GetRollupCapability() => new(RollupKind.None);
    }

    private sealed class SharedFactory : IDbContextFactory<PipelineDbContext>
    {
        private readonly DbContextOptions<PipelineDbContext> _options;
        public SharedFactory(DbContextOptions<PipelineDbContext> options) => _options = options;
        public PipelineDbContext CreateDbContext() => new(_options);
    }
}
