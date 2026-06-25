// The defined input a monitoring cycle hands the linked workflow (feature 013, FR-018).
namespace DBAIAzure.Core.Models;

/// <summary>
/// The defined snapshot of a monitored app that a monitoring cycle inspects and passes to the linked
/// workflow (FR-018) — the app's status plus its most recent run outcome/summary and a secret-redacted
/// log tail. Resolves "what the workflow observes": the app's latest run, not a live process.
/// </summary>
public record MonitoringSnapshot(
    /// <summary>The app being monitored.</summary>
    string AppId,

    /// <summary>App name.</summary>
    string Name,

    /// <summary>Current lifecycle status.</summary>
    AppStatus Status,

    /// <summary>Outcome of the most recent run, or null if never run.</summary>
    RunOutcome? LastRunOutcome,

    /// <summary>One-line summary of the most recent run, or null.</summary>
    string? LastRunSummary,

    /// <summary>Bounded, secret-redacted tail of the most recent run's logs.</summary>
    string RecentLogTail)
{
    /// <summary>Builds a snapshot from an app's current state. Trims the log tail to a bounded length.</summary>
    public static MonitoringSnapshot FromApp(MonitoredApp app, int maxLogChars = 2_000)
    {
        var logs = app.LastRunResult?.Logs ?? string.Empty;
        var tail = logs.Length > maxLogChars ? logs[^maxLogChars..] : logs;
        return new MonitoringSnapshot(
            app.AppId, app.Name, app.Status, app.LastRunResult?.Outcome, app.LastRunResult?.Summary, tail);
    }

    /// <summary>
    /// Whether the snapshot indicates a problem worth raising — a failed/timed-out last run or a
    /// failed build. This is the detection signal the monitoring cycle acts on.
    /// </summary>
    public bool IndicatesProblem =>
        Status == AppStatus.BuildFailed ||
        LastRunOutcome is RunOutcome.Failed or RunOutcome.TimedOut;

    /// <summary>A stable issue type label used in the dedup signature.</summary>
    public string IssueType => Status == AppStatus.BuildFailed ? "build-failed" : $"run-{LastRunOutcome}";

    /// <summary>Natural-language description handed to the workflow as its run input.</summary>
    public string ToWorkflowInput() =>
        $"Monitoring detected an issue with app '{Name}' (status {Status}). " +
        $"Last run: {LastRunOutcome?.ToString() ?? "n/a"} — {LastRunSummary ?? "no summary"}. " +
        $"Recent logs:\n{RecentLogTail}";
}
