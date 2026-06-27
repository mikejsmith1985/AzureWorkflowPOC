# Contract: IWorkflowApprovalNotifier

`DBAIAzure.Core/Interfaces/IWorkflowApprovalNotifier.cs`

Sends an outbound notification when a workflow builder run suspends at a human-approval node.
Distinct from `IHitlNotifier` (pipeline runner) — different signature, different domain.

---

```csharp
public interface IWorkflowApprovalNotifier
{
    /// <summary>
    /// Sends an approval-request notification to the first approver in the chain.
    /// Fire-and-forget from the orchestrator's perspective — failures are logged and
    /// surfaced in the Review Queue but must not block run suspension.
    /// </summary>
    /// <param name="runId">Identifies the run; embedded in the callback payload.</param>
    /// <param name="workflowName">Display name of the workflow being executed.</param>
    /// <param name="nodeLabel">Label of the suspended approval node.</param>
    /// <param name="question">The plain-language question from ApprovalNodeConfig.PromptShown.</param>
    /// <param name="approverChain">Ordered list of approver UPNs; first entry is notified.</param>
    /// <param name="decisionOptions">Labels for the action buttons (e.g. ["Approve", "Reject"]).</param>
    Task NotifyAsync(
        string runId,
        string workflowName,
        string nodeLabel,
        string question,
        IReadOnlyList<string> approverChain,
        IReadOnlyList<string> decisionOptions,
        CancellationToken ct = default);

    /// <summary>
    /// Sends an escalation notification to the next approver in the chain after a timeout.
    /// The currentApproverIndex identifies which approver in the chain timed out.
    /// </summary>
    Task EscalateAsync(
        string runId,
        string workflowName,
        string nodeLabel,
        string question,
        IReadOnlyList<string> approverChain,
        int currentApproverIndex,
        IReadOnlyList<string> decisionOptions,
        CancellationToken ct = default);
}
```

---

**Implementation**: `WorkflowApprovalTeamsNotifier` in `DBAIAzure.Web/Services/`
- Sends a Teams Adaptive Card via Microsoft Graph API (`POST /v1.0/chats/{chatId}/messages` or
  `POST /v1.0/users/{upn}/sendMail` with a fallback).
- Embeds `runId` in the Adaptive Card `Action.Submit` data payload so the webhook receiver can
  route the response.

**Registration**: `services.AddScoped<IWorkflowApprovalNotifier, WorkflowApprovalTeamsNotifier>()`

**Webhook receiver**: `TeamsWebhookController` (minimal API) receives the Adaptive Card action
POST, validates the Microsoft-signed JWT (clarification Q1), extracts `runId` + decision from
the action data, and calls `IWorkflowExecutionOrchestrator.SubmitApproval(runId, approved)`.
