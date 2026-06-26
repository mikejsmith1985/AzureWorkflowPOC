// Holds the single active field tooltip for the current Blazor circuit. An InfoTip icon calls Show()
// with its content + anchor rect on hover; a layout-root portal subscribes to OnChange and renders the
// tooltip at a fixed position so it is never clipped by a parent's overflow (spec-009 US2).
namespace DBAIAzure.Web.Services;

/// <summary>Screen position of the icon a tooltip is anchored to (viewport coordinates, from getRectById).</summary>
public sealed record TooltipAnchor(double Left, double Top, double Bottom, double ViewportHeight);

/// <summary>The content and placement of the currently shown tooltip.</summary>
public sealed record TooltipContext(string Content, string? Example, TooltipAnchor Anchor)
{
    // Show the panel below the icon when the icon sits in the top half of the viewport, otherwise
    // above it — keeps the panel on-screen near the top and bottom edges (the "flip" behaviour).
    private const double ViewportMidpointDivisor = 2;

    /// <summary>True when the tooltip should render below its anchor icon; false to render above.</summary>
    public bool ShowBelow => Anchor.Top < Anchor.ViewportHeight / ViewportMidpointDivisor;
}

/// <summary>
/// Single-active-tooltip coordinator scoped to one session. Decouples the many <c>InfoTip</c> icons
/// from the one portal that renders the panel at the layout root (avoids overflow clipping).
/// </summary>
public interface ITooltipService
{
    /// <summary>The tooltip currently being shown, or null when none is active.</summary>
    TooltipContext? Active { get; }

    /// <summary>Raised whenever <see cref="Active"/> changes so the portal can re-render.</summary>
    event Action? OnChange;

    /// <summary>Shows a tooltip with the given content/example anchored to the supplied rect.</summary>
    void Show(string content, string? example, TooltipAnchor anchor);

    /// <summary>Clears any active tooltip.</summary>
    void Hide();
}

/// <inheritdoc />
public sealed class TooltipService : ITooltipService
{
    /// <inheritdoc />
    public TooltipContext? Active { get; private set; }

    /// <inheritdoc />
    public event Action? OnChange;

    /// <inheritdoc />
    public void Show(string content, string? example, TooltipAnchor anchor)
    {
        Active = new TooltipContext(content, example, anchor);
        OnChange?.Invoke();
    }

    /// <inheritdoc />
    public void Hide()
    {
        Active = null;
        OnChange?.Invoke();
    }
}
