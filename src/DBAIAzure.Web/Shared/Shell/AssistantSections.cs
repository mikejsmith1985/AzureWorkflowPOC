// Stable identifiers for the Blazor SectionOutlet/SectionContent seams the Assistant rail exposes.
// Using the framework's section system (Article VII — framework-first) lets a page render content
// into the shell-level rail while keeping ownership of that content's component references and
// callbacks, so the heavily-coupled Workflow Builder chat can live in the rail without the shell
// taking a dependency on the Builder's services.
namespace DBAIAzure.Web.Shared.Shell;

/// <summary>Named section seams the <c>AssistantPanel</c> rail renders content into.</summary>
public static class AssistantSections
{
    /// <summary>
    /// The rail region the Workflow Builder fills with its existing code-assistant chat panel.
    /// The Builder provides a <c>SectionContent</c> for this name; the rail hosts the matching
    /// <c>SectionOutlet</c> when the Builder is the active route.
    /// </summary>
    public const string BuilderChat = "assistant-builder-chat";
}
