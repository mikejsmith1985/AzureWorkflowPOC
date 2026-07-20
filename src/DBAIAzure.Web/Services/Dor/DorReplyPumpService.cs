// Background service that feeds human chat replies into paused DoR conversations (spec-021 US2). It polls the
// active awaiting-human instances, reads new replies from each run's thread, and submits them to the
// orchestrator, which resumes the workflow. Piggybacks on the wake cadence the SLA sweeper already requires.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Processes.Pipeline;

namespace DBAIAzure.Web.Services.Dor;

/// <summary>
/// Polls awaiting-human DoR instances and delivers any new thread replies to the orchestrator. De-duplicates
/// replies in-process by their id so a reply is submitted exactly once. The poll interval is intentionally
/// coarse — DoR SLAs are measured in hours, so reply latency of tens of seconds is immaterial.
/// </summary>
public sealed class DorReplyPumpService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly IDorWorkflowInstanceStore _instanceStore;
    private readonly DorWorkflowOrchestrator _orchestrator;
    private readonly IChatReplyReader _replyReader;
    private readonly IDorConfigResolver _configResolver;
    private readonly ILogger<DorReplyPumpService> _logger;

    // Reply ids already delivered — prevents re-submitting the same reply across poll cycles.
    private readonly HashSet<string> _processedReplies = new();

    public DorReplyPumpService(
        IDorWorkflowInstanceStore instanceStore,
        DorWorkflowOrchestrator orchestrator,
        IChatReplyReader replyReader,
        IDorConfigResolver configResolver,
        ILogger<DorReplyPumpService> logger)
    {
        _instanceStore = instanceStore;
        _orchestrator = orchestrator;
        _replyReader = replyReader;
        _configResolver = configResolver;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DoR reply pump cycle failed; retrying next interval.");
            }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>
    /// Runs one poll: for each awaiting-human instance, read new thread replies and submit each (once) to the
    /// orchestrator. Returns the number of replies delivered. Public so the pump can be exercised in tests.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configResolver.ResolveActiveAsync(cancellationToken);
        var ignore = config.Comms.IgnoreUserIds;
        var active = await _instanceStore.ListActiveAsync(cancellationToken);
        var delivered = 0;

        foreach (var instance in active)
        {
            if (instance.State is not (DorState.AwaitingResponse or DorState.Escalated))
                continue;

            var replies = await _replyReader.ReadNewRepliesAsync(
                instance.ActiveChannelId, instance.ThreadRef, instance.LastSeenReplyRef, ignore, cancellationToken);

            foreach (var reply in replies)
            {
                if (!_processedReplies.Add(reply.ReplyRef))
                    continue; // already delivered in an earlier cycle

                _orchestrator.SubmitReply(instance.RunId, reply.Text);
                delivered++;
            }
        }

        return delivered;
    }
}
