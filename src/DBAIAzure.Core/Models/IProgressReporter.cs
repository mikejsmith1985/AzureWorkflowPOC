namespace DBAIAzure.Core.Models;

/// <summary>
/// Receives progress events from SK process steps.
/// Registered in the kernel's DI container so steps can emit progress
/// without a direct dependency on the web or runner layer.
/// </summary>
public interface IProgressReporter
{
    /// <summary>Emit a step-level log event (normalisation, DoR verdict, estimate, etc.).</summary>
    void ReportStep(string stepName, string message, ReportLevel level = ReportLevel.Info);

    /// <summary>
    /// Emit a single streaming token so the UI can render LLM output in real time.
    /// Called once per chunk from GetStreamingChatMessageContentsAsync.
    /// </summary>
    void ReportToken(string stepName, string token);

    /// <summary>
    /// Capture the TicketState before and after a step for the state inspector / time travel.
    /// Called by each LLM step after it has produced its output TicketState.
    /// </summary>
    void ReportSnapshot(string stepName, TicketState before, TicketState after);

    /// <summary>Called by ActionStep once the Jira URL and story points are finalised.</summary>
    void ReportComplete(TicketState finalState);
}

public enum ReportLevel { Info, Success, Warning, Error }
