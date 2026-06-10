namespace DBAIAzure.Core.Models;

/// <summary>
/// Receives pipeline progress events from SK process steps.
/// Registered in the kernel's DI container by the orchestrator so steps
/// can call it without a direct dependency on the web or runner layer.
/// </summary>
public interface IProgressReporter
{
    /// <summary>Report a step-level event (normalisation, DoR verdict, estimate, etc.).</summary>
    void ReportStep(string stepName, string message, ReportLevel level = ReportLevel.Info);

    /// <summary>Called by ActionStep once the Jira issue URL and story points are final.</summary>
    void ReportComplete(TicketState finalState);
}

public enum ReportLevel { Info, Success, Warning, Error }
