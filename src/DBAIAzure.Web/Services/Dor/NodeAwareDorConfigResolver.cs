// Overlays the DoR configuration held on the workflow's own nodes onto the connector-row configuration, so what
// an operator edits in the visual builder is what the workflow actually runs.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Core.Models.DorWorkflow.Config;

namespace DBAIAzure.Web.Services.Dor;

/// <summary>
/// The node-config assembler. Decorates the connector-row <see cref="IDorConfigResolver"/> and overlays the
/// configuration slices that now live on the workflow's nodes, so editing a node in the visual builder changes
/// what the workflow does. Today one slice has moved: the Definition-of-Ready document, attached to a node as a
/// Document reference named <see cref="DorDocumentDefaults.ReferenceName"/>. Every other namespace still comes
/// from the connector row until later steps move it onto its owning node.
///
/// <para><b>Precedence</b>: the node wins whenever it carries a non-blank DoR document; otherwise the connector
/// row's DoR settings are used unchanged. This is deliberate — the node is becoming the source of truth, so a
/// document edited on the AI review step must beat a stale card.</para>
///
/// <para>Resolution is best-effort: any repository or parse failure falls back to the connector configuration
/// rather than breaking a run. Secrets are never read from nodes — node config is stored in plain text with the
/// workflow graph, so <see cref="ResolveSecretsAsync"/> always delegates to the encrypted store (Article IX).</para>
/// </summary>
public sealed class NodeAwareDorConfigResolver : IDorConfigResolver
{
    private const string InlineSourceType = "inline";
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

        var nodeDocument = await FindNodeDorDocumentAsync(ct);
        if (string.IsNullOrWhiteSpace(nodeDocument))
            return config;

        _logger.LogInformation(
            "Using the Definition-of-Ready document attached to the workflow node; it overrides the connector card.");

        return config with
        {
            Dor = config.Dor with
            {
                SourceType = InlineSourceType,
                InlineMarkdown = nodeDocument,
                Format = MarkdownFormat,
            },
        };
    }

    /// <inheritdoc/>
    public Task<DorWorkflowSecrets> ResolveSecretsAsync(CancellationToken ct = default)
        => _connectorConfig.ResolveSecretsAsync(ct);

    /// <summary>
    /// Finds the DoR document attached to the active DoR workflow's nodes. The active workflow is the most
    /// recently modified one whose name matches the DoR starter; the document is the first non-blank Document
    /// reference named "Definition of Ready" on any of its nodes, so an operator may move it between steps.
    /// </summary>
    private async Task<string?> FindNodeDorDocumentAsync(CancellationToken ct)
    {
        try
        {
            var workflows = await _workflows.ListByOwnerAsync(DefaultWorkflowProvider.DemoOwnerId, ct);

            var dorWorkflow = workflows
                .Where(workflow => workflow.Name.StartsWith(
                    DefaultWorkflowProvider.DefaultName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(workflow => workflow.LastModifiedAt)
                .FirstOrDefault();

            return dorWorkflow?.Nodes
                .SelectMany(node => NodeReferenceConfig.Read(node.FunctionConfig))
                .Where(reference => reference.Type == NodeReferenceType.Document
                                    && string.Equals(
                                        reference.Name,
                                        DorDocumentDefaults.ReferenceName,
                                        StringComparison.OrdinalIgnoreCase))
                .Select(reference => reference.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Reading the DoR document from the workflow nodes failed; using the connector configuration.");
            return null;
        }
    }
}
