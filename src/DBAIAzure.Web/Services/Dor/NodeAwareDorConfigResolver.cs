// Overlays the DoR configuration held on the workflow's own nodes onto the connector-row configuration, so what
// an operator edits in the visual builder is what the workflow actually runs.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Core.Models.DorWorkflow.Config;

namespace DBAIAzure.Web.Services.Dor;

/// <summary>
/// The node-config assembler. Decorates the connector-row <see cref="IDorConfigResolver"/> and overlays the
/// configuration each workflow node owns, so configuring a step in the visual builder configures the run. Nodes
/// declare what they are via <see cref="DorNodeRole"/>, and each role contributes its slice: the trigger supplies
/// the watched tickets and the dry-run switch, the review step the model settings and the DoR document, the
/// ready/update steps the Jira transition and write whitelist, the resolve/escalate steps their channels, SLAs
/// and iteration budgets, and the audit step what gets recorded.
///
/// <para><b>Precedence</b>: a node value wins whenever it is set; blank strings and empty lists count as unset and
/// fall back to the connector row. This is deliberate — the node is the source of truth, so a step edited in the
/// builder must beat a stale card.</para>
///
/// <para>A workflow whose nodes supply the essentials (DoR document, project keys, ready transition) is treated as
/// configured on its own, so the connector card is no longer required to run.</para>
///
/// <para>Resolution is best-effort: any repository or parse failure falls back to the connector configuration
/// rather than breaking a run. Secrets are never read from nodes — node config is stored in plain text with the
/// workflow graph, so <see cref="ResolveSecretsAsync"/> always delegates to the encrypted store (Article IX).</para>
/// </summary>
public sealed class NodeAwareDorConfigResolver : IDorConfigResolver
{
    private const string InlineSourceType = "inline";
    private const string UrlSourceType = "url";
    private const string MarkdownFormat = "markdown";

    private readonly IDorConfigResolver _connectorConfig;
    private readonly IWorkflowRepository _workflows;
    private readonly ILogger<NodeAwareDorConfigResolver> _logger;

    public NodeAwareDorConfigResolver(
        IDorConfigResolver connectorConfig,
        IWorkflowRepository workflows,
        ILogger<NodeAwareDorConfigResolver> logger)
    {
        _connectorConfig = connectorConfig;
        _workflows = workflows;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<DorWorkflowConfig> ResolveActiveAsync(CancellationToken ct = default)
    {
        var config = await _connectorConfig.ResolveActiveAsync(ct);

        var workflow = await FindActiveDorWorkflowAsync(ct);
        if (workflow is null)
            return config;

        config = ApplyDorDocument(config, workflow);
        config = ApplyNodeSettings(config, workflow);

        // Nodes alone can fully configure the workflow; the connector card is then unnecessary.
        if (!config.IsConfigured && HasEssentials(config))
        {
            _logger.LogInformation("The DoR workflow is configured from its own nodes; the connector card is not required.");
            config = config with { IsConfigured = true };
        }

        return config;
    }

    /// <inheritdoc/>
    public Task<DorWorkflowSecrets> ResolveSecretsAsync(CancellationToken ct = default)
        => _connectorConfig.ResolveSecretsAsync(ct);

    // ── Locating the active workflow ─────────────────────────────────────────────

    /// <summary>
    /// The active DoR workflow is the most recently modified one named for the DoR starter, owner-scoped.
    /// Best-effort: a store failure resolves to null so the connector configuration is used unchanged.
    /// </summary>
    private async Task<WorkflowDefinition?> FindActiveDorWorkflowAsync(CancellationToken ct)
    {
        try
        {
            var workflows = await _workflows.ListByOwnerAsync(DefaultWorkflowProvider.DemoOwnerId, ct);
            return workflows
                .Where(workflow => workflow.Name.StartsWith(
                    DefaultWorkflowProvider.DefaultName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(workflow => workflow.LastModifiedAt)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reading the DoR workflow from the store failed; using the connector configuration.");
            return null;
        }
    }

    // ── The DoR document (a node reference) ──────────────────────────────────────

    /// <summary>Overlays the DoR document attached to any node as a Document reference named "Definition of Ready".</summary>
    private DorWorkflowConfig ApplyDorDocument(DorWorkflowConfig config, WorkflowDefinition workflow)
    {
        var document = workflow.Nodes
            .SelectMany(node => NodeReferenceConfig.Read(node.FunctionConfig))
            .Where(reference => reference.Type == NodeReferenceType.Document
                                && string.Equals(
                                    reference.Name, DorDocumentDefaults.ReferenceName, StringComparison.OrdinalIgnoreCase))
            .Select(reference => reference.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        if (string.IsNullOrWhiteSpace(document))
            return config;

        _logger.LogInformation(
            "Using the Definition-of-Ready document attached to the workflow node; it overrides the connector card.");

        return config with
        {
            Dor = config.Dor with
            {
                SourceType = InlineSourceType,
                InlineMarkdown = document,
                Format = MarkdownFormat,
            },
        };
    }

    // ── The per-role settings slices ─────────────────────────────────────────────

    /// <summary>Collects each node's settings by role and overlays every namespace they contribute.</summary>
    private static DorWorkflowConfig ApplyNodeSettings(DorWorkflowConfig config, WorkflowDefinition workflow)
    {
        var byRole = new Dictionary<DorNodeRole, DorNodeSettings>();
        foreach (var node in workflow.Nodes)
        {
            var settings = DorNodeSettingsConfig.Read(node.FunctionConfig);
            if (settings is not null && settings.Role != DorNodeRole.None)
                byRole[settings.Role] = settings;
        }

        if (byRole.Count == 0)
            return config;

        return config with
        {
            Jira = ApplyJira(config.Jira, byRole),
            Ai = ApplyAi(config.Ai, byRole),
            Comms = ApplyComms(config.Comms, byRole),
            Sla = ApplySla(config.Sla, byRole),
            Audit = ApplyAudit(config.Audit, byRole),
            Run = ApplyRun(config.Run, byRole),
        };
    }

    private static DorJiraConfig ApplyJira(DorJiraConfig jira, Dictionary<DorNodeRole, DorNodeSettings> byRole)
    {
        if (byRole.TryGetValue(DorNodeRole.Trigger, out var trigger))
        {
            jira = jira with
            {
                ProjectKeys = PreferList(trigger.ProjectKeys, jira.ProjectKeys),
                IssueTypes = PreferList(trigger.IssueTypes, jira.IssueTypes),
                WatchFields = PreferList(trigger.WatchFields, jira.WatchFields),
            };
        }

        if (byRole.TryGetValue(DorNodeRole.ReadyTransition, out var ready))
        {
            jira = jira with
            {
                ReadyTransitionId = PreferText(ready.ReadyTransitionId, jira.ReadyTransitionId),
                ReadyStatus = PreferText(ready.ReadyStatus, jira.ReadyStatus),
            };
        }

        if (byRole.TryGetValue(DorNodeRole.Update, out var update))
            jira = jira with { AiEditableFields = PreferList(update.AiEditableFields, jira.AiEditableFields) };

        if (byRole.TryGetValue(DorNodeRole.Escalate, out var escalate))
            jira = jira with { ManualLabel = PreferText(escalate.ManualLabel, jira.ManualLabel) };

        return jira;
    }

    private static DorAiConfig ApplyAi(DorAiConfig ai, Dictionary<DorNodeRole, DorNodeSettings> byRole)
    {
        if (byRole.TryGetValue(DorNodeRole.Review, out var review))
        {
            ai = ai with
            {
                Temperature = review.Temperature ?? ai.Temperature,
                MaxTokens = review.MaxTokens ?? ai.MaxTokens,
                ReviewPromptTemplate = PreferText(review.ReviewPromptTemplate, ai.ReviewPromptTemplate),
            };
        }

        if (byRole.TryGetValue(DorNodeRole.Resolve, out var resolve))
        {
            ai = ai with
            {
                ConversationPromptTemplate =
                    PreferText(resolve.ConversationPromptTemplate, ai.ConversationPromptTemplate),
            };
        }

        if (byRole.TryGetValue(DorNodeRole.Update, out var update))
            ai = ai with { UpdatePromptTemplate = PreferText(update.UpdatePromptTemplate, ai.UpdatePromptTemplate) };

        return ai;
    }

    private static DorCommsConfig ApplyComms(DorCommsConfig comms, Dictionary<DorNodeRole, DorNodeSettings> byRole)
    {
        if (byRole.TryGetValue(DorNodeRole.Resolve, out var resolve))
            comms = comms with { Primary = ApplyChannel(comms.Primary, resolve) };

        if (byRole.TryGetValue(DorNodeRole.Escalate, out var escalate))
            comms = comms with { Escalation = ApplyChannel(comms.Escalation, escalate) };

        return comms;
    }

    private static DorChannelConfig ApplyChannel(DorChannelConfig channel, DorNodeSettings settings) => channel with
    {
        ChannelId = PreferText(settings.ChannelId, channel.ChannelId),
        ReplyTimeoutMinutes = settings.ReplyTimeoutMinutes ?? channel.ReplyTimeoutMinutes,
        MaxIterations = settings.MaxIterations ?? channel.MaxIterations,
    };

    private static DorSlaConfig ApplySla(DorSlaConfig sla, Dictionary<DorNodeRole, DorNodeSettings> byRole)
    {
        if (byRole.TryGetValue(DorNodeRole.Resolve, out var resolve) && resolve.SlaHours is { } primaryHours)
            sla = sla with { PrimarySlaHours = primaryHours };

        if (byRole.TryGetValue(DorNodeRole.Escalate, out var escalate) && escalate.SlaHours is { } escalationHours)
            sla = sla with { EscalationSlaHours = escalationHours };

        return sla;
    }

    private static DorAuditConfig ApplyAudit(DorAuditConfig audit, Dictionary<DorNodeRole, DorNodeSettings> byRole)
    {
        if (!byRole.TryGetValue(DorNodeRole.Audit, out var settings))
            return audit;

        return audit with
        {
            LogAiResponses = settings.LogAiResponses ?? audit.LogAiResponses,
            JiraCommentOnPass = settings.JiraCommentOnPass ?? audit.JiraCommentOnPass,
            JiraCommentOnFail = settings.JiraCommentOnFail ?? audit.JiraCommentOnFail,
            JiraCommentOnEscalation = settings.JiraCommentOnEscalation ?? audit.JiraCommentOnEscalation,
        };
    }

    private static DorRunConfig ApplyRun(DorRunConfig run, Dictionary<DorNodeRole, DorNodeSettings> byRole)
    {
        if (byRole.TryGetValue(DorNodeRole.Trigger, out var trigger) && trigger.DryRun is { } isDryRun)
            return run with { DryRun = isDryRun };

        return run;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether the assembled configuration carries everything a run needs — mirrors the required set the DoR
    /// health check validates, so a node-configured workflow reports healthy without a connector row.
    /// </summary>
    private static bool HasEssentials(DorWorkflowConfig config)
    {
        var hasDocument = string.Equals(config.Dor.SourceType, UrlSourceType, StringComparison.OrdinalIgnoreCase)
            ? !string.IsNullOrWhiteSpace(config.Dor.SourceUri)
            : !string.IsNullOrWhiteSpace(config.Dor.InlineMarkdown);

        return hasDocument
               && config.Jira.ProjectKeys.Count > 0
               && !string.IsNullOrWhiteSpace(config.Jira.ReadyTransitionId);
    }

    /// <summary>A node string wins only when it has content; blank means "not set on the node".</summary>
    private static string PreferText(string? nodeValue, string fallback) =>
        string.IsNullOrWhiteSpace(nodeValue) ? fallback : nodeValue;

    /// <summary>A node list wins only when it has entries; empty means "not set on the node".</summary>
    private static IReadOnlyList<string> PreferList(IReadOnlyList<string>? nodeValue, IReadOnlyList<string> fallback) =>
        nodeValue is { Count: > 0 } ? nodeValue : fallback;
}
