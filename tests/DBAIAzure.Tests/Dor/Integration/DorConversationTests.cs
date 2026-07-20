// Integration test for the conversational HITL path (spec-021 US2 / T048): a not-ready ticket suspends at the
// human gate, a reply resolves the gaps (writing only whitelisted fields + transitioning), and a partial reply
// drives a focused follow-up before resolution. Drives the real MAF graph with fakes + in-memory SQLite.
using System.Text.Json;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Core.Models.DorWorkflow.Config;
using DBAIAzure.Core.Models.WorkTracker;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Checkpointing;
using DBAIAzure.Storage.Repositories;
using DBAIAzure.Web.Services.Dor;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.Dor.Integration;

public sealed class DorConversationTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly SqliteConnection _keepAlive;
    private readonly DbContextOptions<PipelineDbContext> _options;
    private readonly EfDorWorkflowInstanceStore _store;

    public DorConversationTests()
    {
        _keepAlive = new SqliteConnection("Data Source=:memory:");
        _keepAlive.Open();
        _options = new DbContextOptionsBuilder<PipelineDbContext>().UseSqlite(_keepAlive).Options;
        using (var seed = new PipelineDbContext(_options)) seed.Database.EnsureCreated();
        _store = new EfDorWorkflowInstanceStore(new SharedFactory(_options));
    }

    public void Dispose() => _keepAlive.Dispose();

    [Fact]
    public async Task Reply_ResolvesGaps_WritesOnlyWhitelistedFields_AndTransitions()
    {
        var adapter = new RecordingAdapter();
        // The reply resolves the gap but also tempts a non-whitelisted field ("status") — it must be dropped.
        var conversation = new QueueConversation(new ReplyEvaluation(
            Resolved: true, RemainingGaps: Array.Empty<string>(),
            FieldUpdates: new Dictionary<string, string> { ["acceptance_criteria"] = "Given/When/Then", ["status"] = "Done" },
            ReplyMessage: "Thanks!"));
        var orchestrator = BuildOrchestrator(adapter, conversation);

        var run = await orchestrator.StartAsync("SBRO-1");
        await run!.WaitSuspendedAsync().WaitAsync(Timeout);          // waiting for a human
        orchestrator.SubmitReply(run.RunId, "Here are the acceptance criteria: ...");
        await run.Completion.WaitAsync(Timeout);

        var instance = await LoadInstanceAsync("SBRO-1");
        Assert.Equal(DorState.Done, instance.State);
        Assert.Equal(DorOutcome.ResolvedAuto, instance.Outcome);
        Assert.Equal(new[] { "31" }, adapter.Transitions);                         // transitioned to ready
        Assert.Equal(new[] { "acceptance_criteria" }, adapter.WrittenFields);       // "status" dropped by the whitelist
        Assert.NotEmpty(adapter.Comments);                                          // summary comment posted
    }

    [Fact]
    public async Task PartialReply_DrivesFollowUp_ThenResolves()
    {
        var adapter = new RecordingAdapter();
        var conversation = new QueueConversation(
            new ReplyEvaluation(false, new[] { "acceptance_criteria" }, new Dictionary<string, string>(), "Could you add explicit acceptance criteria?"),
            new ReplyEvaluation(true, Array.Empty<string>(), new Dictionary<string, string> { ["acceptance_criteria"] = "AC" }, "Thanks!"));
        var messaging = new CountingMessageDelivery();
        var orchestrator = BuildOrchestrator(adapter, conversation, messaging);

        var run = await orchestrator.StartAsync("SBRO-1");
        await run!.WaitSuspendedAsync().WaitAsync(Timeout);   // first suspension (initial gap outreach)
        orchestrator.SubmitReply(run.RunId, "not sure what you mean");
        await run.WaitSuspendedAsync().WaitAsync(Timeout);    // second suspension (follow-up)
        orchestrator.SubmitReply(run.RunId, "here are the AC");
        await run.Completion.WaitAsync(Timeout);

        var instance = await LoadInstanceAsync("SBRO-1");
        Assert.Equal(DorState.Done, instance.State);
        Assert.Equal(DorOutcome.ResolvedAuto, instance.Outcome);
        Assert.True(messaging.Count >= 2);      // initial gap + focused follow-up
        Assert.Equal(new[] { "31" }, adapter.Transitions);
    }

    [Fact]
    public async Task Conversation_SurvivesRestart_AndResolvesOnLaterReply()
    {
        var factory = new SharedFactory(_options);
        var checkpointStore = new EfCheckpointStore(factory);
        var checkpointManager = CheckpointManager.CreateJson(checkpointStore, new JsonSerializerOptions());

        // First "process": start the run; it reviews (fail), posts gaps, and suspends — checkpointed. No reply.
        var orchestrator1 = BuildCheckpointingOrchestrator(new RecordingAdapter(), ResolvingConversation(), checkpointManager);
        var run1 = await orchestrator1.StartAsync("SBRO-1");
        await run1!.WaitSuspendedAsync().WaitAsync(Timeout);

        var paused = await LoadInstanceAsync("SBRO-1");
        Assert.Equal(DorState.AwaitingResponse, paused.State);
        var checkpoint = await WaitForCheckpointAsync(checkpointStore, run1.RunId);

        // Second "process" (simulated restart): a fresh orchestrator resumes the run from its checkpoint.
        var adapter2 = new RecordingAdapter();
        var orchestrator2 = BuildCheckpointingOrchestrator(adapter2, ResolvingConversation(), checkpointManager);
        var run2 = await orchestrator2.RehydrateAsync(paused, checkpoint);
        await run2!.WaitSuspendedAsync().WaitAsync(Timeout);      // resumed → back in AwaitingResponse

        orchestrator2.SubmitReply(run2.RunId, "here are the acceptance criteria");
        await run2.Completion.WaitAsync(Timeout);

        var final = await LoadInstanceAsync("SBRO-1");
        Assert.Equal(DorState.Done, final.State);
        Assert.Equal(DorOutcome.ResolvedAuto, final.Outcome);      // the later reply still resolved it
        Assert.Equal(new[] { "31" }, adapter2.Transitions);
    }

    [Fact]
    public async Task ReplyPump_DeliversThreadReply_ResumingTheConversation()
    {
        var adapter = new RecordingAdapter();
        var orchestrator = BuildOrchestrator(adapter, ResolvingConversation());
        var run = await orchestrator.StartAsync("SBRO-1");
        await run!.WaitSuspendedAsync().WaitAsync(Timeout);

        // The pump reads a new thread reply and submits it to the orchestrator, resuming the run.
        var pump = new DorReplyPumpService(
            _store, orchestrator, new FakeReplyReader("here are the acceptance criteria"),
            new StubConfigResolver(), NullLogger<DorReplyPumpService>.Instance);
        var delivered = await pump.RunOnceAsync();

        Assert.Equal(1, delivered);
        await run.Completion.WaitAsync(Timeout);

        var instance = await LoadInstanceAsync("SBRO-1");
        Assert.Equal(DorState.Done, instance.State);
        Assert.Equal(DorOutcome.ResolvedAuto, instance.Outcome);
    }

    private sealed class FakeReplyReader : IChatReplyReader
    {
        private readonly string _text;
        public FakeReplyReader(string text) => _text = text;
        public Task<IReadOnlyList<ChatReply>> ReadNewRepliesAsync(
            string channelId, string threadRef, string? afterCursor, IReadOnlyCollection<string> ignoreUserIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ChatReply>>(new[] { new ChatReply("r1", "user", _text, DateTimeOffset.UtcNow) });
    }

    private static QueueConversation ResolvingConversation() => new(new ReplyEvaluation(
        true, Array.Empty<string>(), new Dictionary<string, string> { ["acceptance_criteria"] = "AC" }, "Thanks!"));

    private static async Task<CheckpointInfo> WaitForCheckpointAsync(EfCheckpointStore store, string runId)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (await store.GetLatestCheckpointAsync(runId) is { } checkpoint)
                return checkpoint;
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException($"No checkpoint was written for paused run {runId}.");
    }

    private DorWorkflowOrchestrator BuildCheckpointingOrchestrator(
        RecordingAdapter adapter, QueueConversation conversation, CheckpointManager checkpointManager) =>
        new(
            new FailReviewService(), conversation, adapter, new StubDocumentSource(), new StubConfigResolver(),
            new CountingMessageDelivery(), _store, NullLogger<DorWorkflowOrchestrator>.Instance, checkpointManager);

    // ── Wiring ──────────────────────────────────────────────────────────────────

    private DorWorkflowOrchestrator BuildOrchestrator(
        RecordingAdapter adapter, QueueConversation conversation, IMessageDelivery? messaging = null) =>
        new(
            new FailReviewService(),
            conversation,
            adapter,
            new StubDocumentSource(),
            new StubConfigResolver(),
            messaging ?? new CountingMessageDelivery(),
            _store,
            NullLogger<DorWorkflowOrchestrator>.Instance);

    private async Task<DorWorkflowInstance> LoadInstanceAsync(string ticketKey)
    {
        await using var db = new PipelineDbContext(_options);
        var e = await db.DorWorkflowInstances.AsNoTracking().FirstAsync(x => x.TicketKey == ticketKey);
        return new DorWorkflowInstance
        {
            RunId = e.RunId, TicketKey = e.TicketKey, State = (DorState)e.State,
            Outcome = e.Outcome is { } o ? (DorOutcome)o : null, PrimaryIterations = e.PrimaryIterations,
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

    private sealed class QueueConversation : IDorConversationService
    {
        private readonly Queue<ReplyEvaluation> _results;
        public QueueConversation(params ReplyEvaluation[] results) => _results = new Queue<ReplyEvaluation>(results);
        public Task<ReplyEvaluation> EvaluateReplyAsync(IReadOnlyList<string> outstandingGaps, string humanReply, int iteration, DorAiConfig ai, CancellationToken ct = default) =>
            Task.FromResult(_results.Dequeue());
    }

    private sealed class StubDocumentSource : IDorDocumentSource
    {
        public Task<DorDocument> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult(new DorDocument("DOR DOC", "v1", DateTimeOffset.UtcNow, "inline"));
    }

    private sealed class StubConfigResolver : IDorConfigResolver
    {
        public Task<DorWorkflowConfig> ResolveActiveAsync(CancellationToken ct = default) =>
            Task.FromResult(new DorWorkflowConfig
            {
                IsConfigured = true,
                Jira = new DorJiraConfig
                {
                    ProjectKeys = new[] { "SBRO" }, WatchFields = new[] { "summary" },
                    AiEditableFields = new[] { "acceptance_criteria" }, ReadyTransitionId = "31", ReadyStatus = "Ready",
                },
                Comms = new DorCommsConfig { Primary = new DorChannelConfig { MaxIterations = 3 } },
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
        public List<string> WrittenFields { get; } = [];
        public List<string> Comments { get; } = [];
        public string TrackerKey => "Fake";

        public Task<WorkItemFields> ReadWorkItemAsync(WorkItemRef item, IReadOnlyCollection<string> watchFields, CancellationToken ct = default) =>
            Task.FromResult(new WorkItemFields(item.Value, $"https://x/browse/{item.Value}", new Dictionary<string, string?> { ["summary"] = "x" }));
        public Task<string> TransitionAsync(WorkItemRef item, string transitionId, CancellationToken ct = default)
        {
            Transitions.Add(transitionId);
            return Task.FromResult(transitionId);
        }
        public Task SetFieldsAsync(WorkItemRef item, IReadOnlyDictionary<string, object?> logicalFields, CancellationToken ct = default)
        {
            WrittenFields.AddRange(logicalFields.Keys);
            return Task.CompletedTask;
        }
        public Task AppendCommentAsync(WorkItemRef item, string comment, CancellationToken ct = default)
        {
            Comments.Add(comment);
            return Task.CompletedTask;
        }
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
