// Runs a linked saved workflow as the monitor for an app; closes the loop on detected problems (feature 013, US3).
using System.Security.Cryptography;
using System.Text;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Processes.Monitoring;

/// <summary>
/// Runs one monitoring cycle for a linked app. Builds a <see cref="MonitoringSnapshot"/> of the app
/// (status + latest run outcome/summary + redacted log tail, FR-018) and, when it indicates a NEW
/// problem, creates a bounded workflow run via the existing <see cref="IWorkflowExecutionOrchestrator"/>
/// — the same path any other run uses (FR-011) — de-duplicated by issue signature so a recurring
/// problem is raised once (FR-012). The .NET analogue of the reference application's production
/// monitoring trigger: "a detected problem is just another intake." A missing/deleted linked workflow
/// is handled gracefully (FR-017).
/// </summary>
public sealed class AppMonitoringService : IAppMonitoringService
{
    private readonly IWorkflowExecutionOrchestrator _orchestrator;
    private readonly IWorkflowRepository _workflows;
    private readonly IAppHeartbeatStore _heartbeats;

    /// <summary>Creates the service over the orchestrator, workflow repository, and heartbeat store.</summary>
    public AppMonitoringService(
        IWorkflowExecutionOrchestrator orchestrator,
        IWorkflowRepository workflows,
        IAppHeartbeatStore heartbeats)
    {
        _orchestrator = orchestrator;
        _workflows = workflows;
        _heartbeats = heartbeats;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> RunCycleAsync(MonitoredApp app, CancellationToken ct = default)
    {
        // Not monitored, or a non-GUID/deleted link → nothing to do (FR-017, no crash).
        if (string.IsNullOrWhiteSpace(app.LinkedWorkflowId) || !Guid.TryParse(app.LinkedWorkflowId, out var workflowId))
            return Array.Empty<string>();

        var workflow = await _workflows.GetAsync(workflowId, app.OwnerId, ct);
        if (workflow is null)
            return Array.Empty<string>();

        var snapshot = MonitoringSnapshot.FromApp(app);
        if (!snapshot.IndicatesProblem)
            return Array.Empty<string>();

        // Stable signature for this ongoing problem (app + issue type) — recurring issues dedup (FR-012).
        var signature = Signature(app.AppId, snapshot.IssueType);
        if (await _heartbeats.IsRaisedAsync(signature, ct))
            return Array.Empty<string>();

        var runId = await _orchestrator.StartRunAsync(workflow, snapshot.ToWorkflowInput(), ct);
        await _heartbeats.RecordRaisedAsync(new AppRaisedIssue(signature, app.AppId, runId, DateTimeOffset.UtcNow), ct);
        return new[] { runId };
    }

    private static string Signature(string appId, string issueType)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes($"{appId}|{issueType}"));
        return Convert.ToHexString(bytes);
    }
}
