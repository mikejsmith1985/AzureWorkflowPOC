// DoR rule: a node that owns a run-blocking setting must carry it whenever no connector row can supply it (T071).

using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.DorWorkflow;

namespace DBAIAzure.Web.Rules;

/// <summary>
/// Blocks a run when a DoR step is missing a setting that nothing else can supply.
/// <para>
/// Three settings have no safe default, so their absence is caught here rather than half-way through a run
/// against a real ticket: an <see cref="DorNodeRole.Update"/> step with no field whitelist may write nothing
/// (the whitelist is enforced in code — FR-021), a <see cref="DorNodeRole.ReadyTransition"/> step with no
/// transition id has nowhere to move a passing ticket, and a <see cref="DorNodeRole.Resolve"/> or
/// <see cref="DorNodeRole.Escalate"/> step with no channel has nobody to talk to.
/// </para>
/// <para>
/// <b>A blank node field is not by itself a misconfiguration.</b> <c>NodeAwareDorConfigResolver</c> overlays node
/// settings onto the DoR connector row and treats blank as "not set on the node", so a step left empty inherits
/// the row's value — the shipped starter workflow relies on exactly that for its channels. This rule therefore
/// only blocks when there is no configured <see cref="ConnectorType.DorWorkflow"/> row to inherit from, which is
/// the one case where a blank field really does mean the value is nowhere to be found.
/// </para>
/// <para>
/// Trigger, Review and Audit are exempt in every case: each of their fields has a working default, so leaving one
/// unset is a deliberate "use the default" rather than an incomplete step.
/// </para>
/// </summary>
public sealed class DorNodesConfiguredRule : IWorkflowReadinessRule
{
    public string RuleName    => "dor-nodes-configured";
    public string Description => "Every DoR step must carry the settings its run cannot proceed without.";

    public Task<DorRuleResult> CheckAsync(
        WorkflowDefinition workflow,
        IReadOnlyList<ConnectorConfig> connectors,
        CancellationToken ct = default)
    {
        // A configured DoR row supplies a fallback for every field below, so nothing on a node can be "missing".
        var hasConnectorFallback = connectors.Any(c => c.Type == ConnectorType.DorWorkflow && c.IsConfigured);
        if (hasConnectorFallback)
            return Task.FromResult(Passed());

        var problems = workflow.Nodes
            .Select(node => new { Node = node, Settings = DorNodeSettingsConfig.Read(node.FunctionConfig) })
            .Where(pair => pair.Settings is not null && pair.Settings.Role != DorNodeRole.None)
            .Select(pair => DescribeMissingSetting(pair.Node, pair.Settings!))
            .Where(problem => problem is not null)
            .ToList();

        if (problems.Count == 0)
            return Task.FromResult(Passed());

        var reason = $"The following DoR steps are not fully configured: {string.Join("; ", problems)}. " +
                     "Open each step in the workflow builder and complete its settings, " +
                     "or configure the DoR Workflow connector to supply them for every step.";

        return Task.FromResult(new DorRuleResult(Passed: false, RuleName: RuleName, FailureReason: reason));
    }

    private DorRuleResult Passed() => new(Passed: true, RuleName: RuleName, FailureReason: null);

    /// <summary>
    /// Names the one setting this node's role requires but does not have, or null when the node is complete.
    /// Roles absent from the switch carry no run-blocking requirement.
    /// </summary>
    private static string? DescribeMissingSetting(WorkflowNode node, DorNodeSettings settings)
    {
        var nodeName = node.Label ?? node.Id;

        return settings.Role switch
        {
            DorNodeRole.Update when !HasAnyValue(settings.AiEditableFields)
                => $"'{nodeName}' has no editable-field whitelist, so it could not write anything",

            DorNodeRole.ReadyTransition when string.IsNullOrWhiteSpace(settings.ReadyTransitionId)
                => $"'{nodeName}' has no ready transition id, so a passing ticket has nowhere to move",

            DorNodeRole.Resolve or DorNodeRole.Escalate when string.IsNullOrWhiteSpace(settings.ChannelId)
                => $"'{nodeName}' has no channel, so it has nobody to message",

            _ => null,
        };
    }

    /// <summary>True when the list holds at least one non-blank entry; null and empty both count as unset.</summary>
    private static bool HasAnyValue(IReadOnlyList<string>? values) =>
        values is not null && values.Any(value => !string.IsNullOrWhiteSpace(value));
}
