# Contract: IWorkflowReadinessRule + IWorkflowPreRunValidator

`DBAIAzure.Core/Interfaces/IWorkflowReadinessRule.cs`
`DBAIAzure.Core/Interfaces/IWorkflowPreRunValidator.cs`

Strategy contract for Definition of Ready evaluation before a workflow run is submitted.

---

```csharp
/// <summary>
/// One configurable Definition of Ready rule evaluated before a workflow run starts.
/// Rules are registered via DI and resolved by IWorkflowPreRunValidator.
/// </summary>
public interface IWorkflowReadinessRule
{
    /// <summary>
    /// Short name used in DorRuleSettings.DisabledRuleNames to toggle this rule off.
    /// Must be unique across all registered rules.
    /// </summary>
    string RuleName { get; }

    /// <summary>Human-readable description shown in the settings UI.</summary>
    string Description { get; }

    /// <summary>
    /// Evaluates the rule against the workflow definition and the current connector health state.
    /// Returns a passing or failing DorRuleResult — never throws.
    /// </summary>
    Task<DorRuleResult> CheckAsync(
        WorkflowDefinition workflow,
        IReadOnlyList<ConnectorInstance> connectors,
        CancellationToken ct = default);
}

/// <summary>Outcome of one DoR rule evaluation.</summary>
public record DorRuleResult(bool Passed, string RuleName, string? FailureReason);
```

```csharp
/// <summary>
/// Evaluates all registered IWorkflowReadinessRule implementations and aggregates results.
/// Skips rules whose RuleName appears in DorRuleSettings.DisabledRuleNames.
/// </summary>
public interface IWorkflowPreRunValidator
{
    /// <summary>
    /// Returns results for all enabled rules. A workflow may run only when every result
    /// has Passed = true. Results are ordered: failing rules first for UI prominence.
    /// </summary>
    Task<IReadOnlyList<DorRuleResult>> ValidateAsync(
        WorkflowDefinition workflow,
        CancellationToken ct = default);
}
```

---

**Default rule implementations** (all in `DBAIAzure.Web/Rules/`):

| Class | RuleName | FR |
|-------|----------|----|
| `TriggerNodePresentRule` | `trigger-node-present` | FR-24.2 |
| `AllNodesRealizedRule` | `all-nodes-realized` | FR-24.2 |
| `ConnectorsHealthyRule` | `connectors-healthy` | FR-24.2 |
| `ApprovalNodesConfiguredRule` | `approval-nodes-configured` | FR-24.2 |

**Registration helper** (extension method in `DBAIAzure.Web`):
```csharp
services.AddWorkflowReadinessRule<TriggerNodePresentRule>();
services.AddWorkflowReadinessRule<AllNodesRealizedRule>();
services.AddWorkflowReadinessRule<ConnectorsHealthyRule>();
services.AddWorkflowReadinessRule<ApprovalNodesConfiguredRule>();
services.AddScoped<IWorkflowPreRunValidator, WorkflowPreRunValidator>();
```

**Usage**: `WorkflowBuilder.razor` calls `IWorkflowPreRunValidator.ValidateAsync` before enabling
the Run button; failing results are shown as a blocking list above the button.
