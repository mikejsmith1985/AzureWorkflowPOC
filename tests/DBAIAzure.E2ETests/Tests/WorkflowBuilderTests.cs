// Validates the Visual Workflow Builder's key interactive elements.
// If the canvas, palette, toolbar, or chat panel fail to render this suite catches it.
using DBAIAzure.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace DBAIAzure.E2ETests.Tests;

/// <summary>
/// End-to-end tests for the Visual Workflow Builder page (/workflow-builder).
/// Navigates to /workflow-builder/new so the example workflow loads directly,
/// bypassing the first-run entry choice modal (which only shows for an empty DB).
/// Covers canvas render, node palette, toolbar, chat toggle, and run button.
/// </summary>
public sealed class WorkflowBuilderTests : E2ETestBase
{
    // /workflow-builder/new — "new" is not a valid GUID so the page falls through to
    // BuildExampleWorkflow(), which loads a 4-node example without the entry-choice modal.
    private const string BuilderUrl = "/workflow-builder/new";

    public WorkflowBuilderTests(WebAppFixture webApp, PlaywrightFixture playwright)
        : base(webApp, playwright) { }

    [Fact]
    public async Task CanvasContainer_IsVisible_OnLoad()
    {
        await NavigateAsync(BuilderUrl);
        await WaitForToolbarAsync();

        // WorkflowCanvas.razor renders with id="workflow-canvas-drop-zone".
        // Use a JS-evaluated dimension check so Tailwind Play CDN's async CSS injection
        // does not cause a race with Playwright's static visibility heuristic.
        await Page.WaitForFunctionAsync(@"
            () => {
                const el = document.getElementById('workflow-canvas-drop-zone');
                return el !== null && el.getBoundingClientRect().height > 0;
            }");
    }

    [Fact]
    public async Task NodePalette_IsVisible_OnLoad()
    {
        await NavigateAsync(BuilderUrl);
        await WaitForToolbarAsync();

        // WorkflowNodePalette.razor renders a div.workflow-palette containing palette-entry items.
        var palette = Page.Locator(".workflow-palette").First;
        await palette.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 10_000 });

        // At least one draggable palette entry must be present.
        var entry = Page.Locator("div.palette-entry").First;
        await entry.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 5_000 });
    }

    [Fact]
    public async Task Toolbar_WorkflowNameInput_IsVisible()
    {
        await NavigateAsync(BuilderUrl);
        await WaitForToolbarAsync();

        // The toolbar shows the workflow name as a clickable span in display mode.
        // aria-label="Rename workflow — click to edit" is set on that span.
        var nameSpan = Page.Locator("span[aria-label='Rename workflow — click to edit']").First;
        await nameSpan.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    [Fact]
    public async Task ChatToggleButton_Click_OpensChatPanel()
    {
        await NavigateAsync(BuilderUrl);
        await WaitForToolbarAsync();

        // The toolbar has a chat toggle button; aria-label is "Open chat panel" when closed.
        var toggle = Page.Locator("button[aria-label='Open chat panel']").First;
        await toggle.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await toggle.ClickAsync();

        // After clicking, the chat panel <aside> becomes visible.
        var chatPanel = Page.Locator("aside.workflow-chat-panel").First;
        await chatPanel.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    [Fact]
    public async Task RunButton_IsPresent_OnLoad()
    {
        await NavigateAsync(BuilderUrl);
        await WaitForToolbarAsync();

        // The example workflow has all nodes configured, so CanRun = true and
        // the Run button has class "run-btn-ready". It must be present in the toolbar.
        var runBtn = Page.Locator(".workflow-toolbar .run-btn-ready").First;
        await runBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Waits for the toolbar to be visible, confirming Blazor has completed its first
    /// interactive render pass. Must be called before asserting any page element.
    /// </summary>
    private async Task WaitForToolbarAsync()
    {
        await Page.Locator(".workflow-toolbar").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
    }
}
