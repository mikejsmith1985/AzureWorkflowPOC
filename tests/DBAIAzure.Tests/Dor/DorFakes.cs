// Reusable test doubles + fixtures for the DoR workflow integration tests (spec-021). Keeps the resilience and
// dry-run tests compact by sharing the fakes.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Core.Models.DorWorkflow.Config;
using DBAIAzure.Core.Models.WorkTracker;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DBAIAzure.Tests.Dor;

/// <summary>An in-memory SQLite instance store with a load helper, disposed at test end.</summary>
public sealed class DorStoreFixture : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly DbContextOptions<PipelineDbContext> _options;

    public DorStoreFixture()
    {
        _keepAlive = new SqliteConnection("Data Source=:memory:");
        _keepAlive.Open();
        _options = new DbContextOptionsBuilder<PipelineDbContext>().UseSqlite(_keepAlive).Options;
        using (var seed = new PipelineDbContext(_options)) seed.Database.EnsureCreated();
        Store = new EfDorWorkflowInstanceStore(new Factory(_options));
    }

    public EfDorWorkflowInstanceStore Store { get; }

    public async Task<DorWorkflowInstance> LoadAsync(string ticketKey)
    {
        await using var db = new PipelineDbContext(_options);
        var e = await db.DorWorkflowInstances.AsNoTracking().FirstAsync(x => x.TicketKey == ticketKey);
        return new DorWorkflowInstance
        {
            RunId = e.RunId, TicketKey = e.TicketKey, State = (DorState)e.State,
            SlaTier = (SlaTier)e.SlaTier, Outcome = e.Outcome is { } o ? (DorOutcome)o : null,
            PrimaryIterations = e.PrimaryIterations,
        };
    }

    public void Dispose() => _keepAlive.Dispose();

    private sealed class Factory : IDbContextFactory<PipelineDbContext>
    {
        private readonly DbContextOptions<PipelineDbContext> _options;
        public Factory(DbContextOptions<PipelineDbContext> options) => _options = options;
        public PipelineDbContext CreateDbContext() => new(_options);
    }
}

/// <summary>Standard DoR config for tests, with dry-run and SLA knobs.</summary>
public static class DorTestConfig
{
    public static DorWorkflowConfig Standard(bool dryRun = false, double primarySlaHours = 24) => new()
    {
        IsConfigured = true,
        Jira = new DorJiraConfig
        {
            ProjectKeys = new[] { "SBRO" }, WatchFields = new[] { "summary" },
            AiEditableFields = new[] { "acceptance_criteria" }, ReadyTransitionId = "31",
            ReadyStatus = "Ready", ManualLabel = "dor-manual-required",
        },
        Sla = new DorSlaConfig { ClockType = "wall_clock", PrimarySlaHours = primarySlaHours, EscalationSlaHours = 8 },
        Comms = new DorCommsConfig
        {
            Primary = new DorChannelConfig { ChannelId = "#dor", MaxIterations = 3 },
            Escalation = new DorChannelConfig { ChannelId = "#esc", MaxIterations = 2 },
        },
        Run = new DorRunConfig { DryRun = dryRun },
    };
}

/// <summary>A work-tracker adapter that records writes and can be told to fail the ticket read.</summary>
public sealed class RecordingDorAdapter : IWorkTrackerAdapter
{
    public bool ThrowOnRead { get; set; }
    public List<string> Transitions { get; } = [];
    public List<string> WrittenFields { get; } = [];
    public List<string> Comments { get; } = [];
    public string TrackerKey => "Fake";

    public Task<WorkItemFields> ReadWorkItemAsync(WorkItemRef item, IReadOnlyCollection<string> watchFields, CancellationToken ct = default)
    {
        if (ThrowOnRead) throw new HttpRequestException("Jira unreachable.");
        return Task.FromResult(new WorkItemFields(item.Value, $"https://x/browse/{item.Value}", new Dictionary<string, string?> { ["summary"] = "x" }));
    }
    public Task<string> TransitionAsync(WorkItemRef item, string transitionId, CancellationToken ct = default) { Transitions.Add(transitionId); return Task.FromResult(transitionId); }
    public Task SetFieldsAsync(WorkItemRef item, IReadOnlyDictionary<string, object?> logicalFields, CancellationToken ct = default) { WrittenFields.AddRange(logicalFields.Keys); return Task.CompletedTask; }
    public Task AppendCommentAsync(WorkItemRef item, string comment, CancellationToken ct = default) { Comments.Add(comment); return Task.CompletedTask; }
    public Task<CreatedWorkItemRef> CreateWorkItemAsync(WorkItemType type, string title, string description, WorkItemRef? parent, CancellationToken ct = default) =>
        Task.FromResult(new CreatedWorkItemRef { WorkItemId = new WorkItemRef("X-1"), WorkItemType = "Task", Url = "", WasUpdated = false });
    public Task<CreatedWorkItemRef> UpsertWorkItemAsync(WorkItemRef item, string title, string description, string appendComment, CancellationToken ct = default) =>
        Task.FromResult(new CreatedWorkItemRef { WorkItemId = item, WorkItemType = "", Url = "", WasUpdated = true });
    public Task<WorkItemRef?> ResolveByBindingKeyAsync(string bindingKey, CancellationToken ct = default) => Task.FromResult<WorkItemRef?>(null);
    public Task<ProvisioningResult> ProvisionFieldsAsync(AdoTelemetryFieldConfig fieldConfig, CancellationToken ct = default) => Task.FromResult(new ProvisioningResult { IsSuccess = true, Mode = "Fake" });
    public RollupCapability GetRollupCapability() => new(RollupKind.None);
}

/// <summary>A review service returning a fixed verdict, or always throwing (to simulate an AI failure).</summary>
public sealed class FixedReview : IDorReviewService
{
    private readonly DorReviewResult? _result;
    public FixedReview(DorReviewResult? result) => _result = result;
    public static FixedReview Fail(params string[] gaps) => new(new DorReviewResult("FAIL",
        gaps.Select(g => new CriterionResult(g, "FAIL", "missing")).ToArray(), gaps, new Dictionary<string, string>()));
    public Task<DorReviewResult> ReviewAsync(IReadOnlyDictionary<string, string?> ticketFields, string dorDocument, DorAiConfig ai, CancellationToken ct = default) =>
        _result is null ? throw new HttpRequestException("AI provider unavailable.") : Task.FromResult(_result);
}

/// <summary>A conversation service returning queued evaluations.</summary>
public sealed class QueueConversationSvc : IDorConversationService
{
    private readonly Queue<ReplyEvaluation> _results;
    public QueueConversationSvc(params ReplyEvaluation[] results) => _results = new Queue<ReplyEvaluation>(results);
    public Task<ReplyEvaluation> EvaluateReplyAsync(IReadOnlyList<string> outstandingGaps, string humanReply, int iteration, DorAiConfig ai, CancellationToken ct = default) =>
        Task.FromResult(_results.Dequeue());
}

/// <summary>A DoR document source returning fixed text, or always failing to load.</summary>
public sealed class FakeDoc : IDorDocumentSource
{
    private readonly string? _text;
    public FakeDoc(string? text) => _text = text;
    public Task<DorDocument> LoadAsync(CancellationToken ct = default) =>
        _text is null ? throw new DorDocumentUnavailableException("unreachable") : Task.FromResult(new DorDocument(_text, "v1", DateTimeOffset.UtcNow, "inline"));
}

/// <summary>A message delivery that counts sends.</summary>
public sealed class CountingMessaging : IMessageDelivery
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

/// <summary>A config resolver returning a fixed configuration.</summary>
public sealed class FixedConfig : IDorConfigResolver
{
    private readonly DorWorkflowConfig _config;
    public FixedConfig(DorWorkflowConfig config) => _config = config;
    public Task<DorWorkflowConfig> ResolveActiveAsync(CancellationToken ct = default) => Task.FromResult(_config);
    public Task<DorWorkflowSecrets> ResolveSecretsAsync(CancellationToken ct = default) => Task.FromResult(new DorWorkflowSecrets(null, null, null, null));
}
