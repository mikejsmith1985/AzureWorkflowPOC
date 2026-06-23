// SignalR hub for real-time workflow run state updates (FR-20.4, FR-21.5).

using Microsoft.AspNetCore.SignalR;

namespace DBAIAzure.Web.Hubs;

/// <summary>
/// Pushes workflow run state-change events to connected browser clients.
/// The Review Queue and Execution History pages subscribe to receive updates without
/// requiring a manual page reload (FR-20.4). Clients join a per-run group so they
/// only receive updates for runs they are actively viewing.
/// </summary>
public sealed class WorkflowRunHub : Hub
{
    /// <summary>
    /// Joins the caller to the per-run group so they receive targeted updates for that run.
    /// Called by the client immediately after connecting and navigating to a run view.
    /// </summary>
    public Task SubscribeToRun(string runId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, RunGroup(runId));

    /// <summary>
    /// Removes the caller from a per-run group when they navigate away.
    /// </summary>
    public Task UnsubscribeFromRun(string runId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, RunGroup(runId));

    /// <summary>Stable group name for a given run id.</summary>
    public static string RunGroup(string runId) => $"run:{runId}";

    /// <summary>Group name used to broadcast queue-level refresh notifications.</summary>
    public const string ReviewQueueGroup = "review-queue";
}
