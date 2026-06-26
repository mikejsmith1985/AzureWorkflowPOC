// The single source of truth for the Admin Console sidebar and section sub-tabs. Both the sidebar
// and the per-section tab strip render from this one model, so the two navigation surfaces can never
// drift apart. Routes here are the EXISTING page routes (preserved by the redesign) grouped into the
// five reference sections plus a separated User Guide.
namespace DBAIAzure.Web.Navigation;

/// <summary>
/// A view reachable inside a section. When a section has more than one, each renders as a sub-tab.
/// </summary>
/// <param name="Label">Human label shown on the sidebar row or sub-tab.</param>
/// <param name="Route">The existing page route this sub-view maps to (routes are preserved).</param>
/// <param name="MatchPrefix">
/// Optional route prefix that keeps this sub-view active on its detail pages (for example
/// <c>/runs</c> staying active on <c>/runs/{id}</c>). Falls back to <see cref="Route"/>.
/// </param>
public sealed record NavSubView(string Label, string Route, string? MatchPrefix = null)
{
    /// <summary>The prefix used to decide whether the current URL activates this sub-view.</summary>
    public string ActivePrefix => MatchPrefix ?? Route;
}

/// <summary>A sidebar section grouping one or more related views under a single icon and label.</summary>
/// <param name="Key">Stable identifier (for example <c>monitor</c>), used for active-state comparison.</param>
/// <param name="Label">Canonical reference label shown in the sidebar.</param>
/// <param name="Icon">Icon identifier for the sidebar row (resolved by the sidebar component).</param>
/// <param name="SubViews">Ordered sub-views; a single entry means the section maps to one screen.</param>
/// <param name="IsSecondary">When true the row renders below a separator (used for the User Guide).</param>
public sealed record NavSection(
    string Key,
    string Label,
    string Icon,
    IReadOnlyList<NavSubView> SubViews,
    bool IsSecondary = false)
{
    /// <summary>The route the sidebar row links to — the section's first sub-view.</summary>
    public string PrimaryRoute => SubViews[0].Route;

    /// <summary>True when the section exposes more than one sub-view (and so renders a sub-tab strip).</summary>
    public bool HasSubTabs => SubViews.Count > 1;
}

/// <summary>
/// The fixed Admin Console navigation: five primary sections plus a separated User Guide. Every
/// pre-redesign route appears exactly once so no screen is orphaned (FR-012 / SC-003).
/// </summary>
public static class NavModel
{
    /// <summary>All sidebar sections in display order.</summary>
    public static readonly IReadOnlyList<NavSection> Sections = new[]
    {
        new NavSection("monitor", "Monitor", "activity", new[]
        {
            new NavSubView("Threads", "/"),
            new NavSubView("Run History", "/runs", MatchPrefix: "/run"),
        }),
        new NavSection("review", "Review Queue", "inbox", new[]
        {
            new NavSubView("Review Queue", "/review-queue"),
        }),
        new NavSection("automation", "Automation", "workflow", new[]
        {
            new NavSubView("Workflow Builder", "/workflow-builder"),
            new NavSubView("Workflow Gallery", "/workflow-gallery"),
        }),
        new NavSection("configuration", "Configuration", "sliders", new[]
        {
            new NavSubView("Connectors", "/settings/connectors"),
        }),
        new NavSection("repos", "Repos & Apps", "grid", new[]
        {
            new NavSubView("Apps", "/apps"),
        }),
        new NavSection("guide", "User Guide", "book", new[]
        {
            new NavSubView("User Guide", "/user-guide"),
        }, IsSecondary: true),
    };

    /// <summary>
    /// Finds the section whose sub-view best matches the current relative path (most specific prefix
    /// wins, so "/runs" beats the root "/"). Returns null when nothing matches.
    /// </summary>
    /// <param name="relativePath">The current path beginning with "/", without query or fragment.</param>
    public static NavSection? ActiveSection(string relativePath)
    {
        NavSection? best = null;
        var bestLength = -1;
        foreach (var section in Sections)
        {
            foreach (var subView in section.SubViews)
            {
                if (!IsMatch(relativePath, subView.ActivePrefix)) continue;
                if (subView.ActivePrefix.Length <= bestLength) continue;
                best = section;
                bestLength = subView.ActivePrefix.Length;
            }
        }
        return best;
    }

    /// <summary>True when the path equals the prefix, or (for non-root prefixes) starts with it.</summary>
    private static bool IsMatch(string relativePath, string prefix)
    {
        if (string.Equals(relativePath, prefix, StringComparison.OrdinalIgnoreCase)) return true;
        if (prefix == "/") return relativePath == "/";
        return relativePath.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
    }
}
