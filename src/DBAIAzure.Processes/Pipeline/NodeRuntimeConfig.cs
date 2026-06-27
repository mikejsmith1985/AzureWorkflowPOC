// Per-node configuration handed to each workflow step instance at build time via SK step state,
// so a step executes from its realized configuration rather than a generic fallback.

namespace DBAIAzure.Processes.Pipeline;

/// <summary>
/// The slice of a canvas node a runtime step needs to execute itself: its identity, its plain-language
/// goal (fallback for un-realized nodes), its realized <c>FunctionConfig</c> envelope JSON (null until
/// the node is made real), and its output-port labels (used by branch steps for routing). Carried as
/// Semantic Kernel step state — one instance per node — so every step is configured independently.
/// Must be a reference type with a parameterless constructor to satisfy <c>KernelProcessStep&lt;TState&gt;</c>.
/// </summary>
public sealed class NodeRuntimeConfig
{
    /// <summary>The canvas node id this step represents.</summary>
    public string? NodeId { get; set; }

    /// <summary>The node's plain-language goal — used when no realized config is present.</summary>
    public string? GoalPrompt { get; set; }

    /// <summary>The realized configuration envelope JSON (see <c>NodeConfigSerializer</c>); null until realized.</summary>
    public string? FunctionConfig { get; set; }

    /// <summary>Output-port labels for this node, in canvas order — branch steps route among these.</summary>
    public List<string> OutputPortLabels { get; set; } = new();
}
