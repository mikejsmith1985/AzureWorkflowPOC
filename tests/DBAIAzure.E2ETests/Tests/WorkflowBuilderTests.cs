// Validates the Visual Workflow Builder by simulating real user actions — clicking, typing,
// and interacting with the canvas exactly as a user would. No test stops at "is this element
// present?"; every test verifies that the interaction produces the expected state change.
using DBAIAzure.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace DBAIAzure.E2ETests.Tests;

/// <summary>
/// End-to-end tests for the Visual Workflow Builder page (/workflow-builder).
/// Navigates to /workflow-builder/new so the 4-node example workflow loads directly,
/// bypassing the first-run entry choice modal. Tests drive real user interactions and
/// assert on visible state changes — not element presence alone.
/// </summary>
public sealed class WorkflowBuilderTests : E2ETestBase
{
    // /workflow-builder/new — "new" is not a valid GUID so the page falls through to
    // BuildExampleWorkflow(), which loads a 4-node example (all configured, CanRun=true)
    // without querying the DB or showing the entry-choice modal.
    private const string BuilderUrl = "/workflow-builder/new";

    public WorkflowBuilderTests(WebAppFixture webApp, PlaywrightFixture playwright)
        : base(webApp, playwright) { }

    [Fact]
    public async Task WorkflowName_ClickToRename_CommitsNewName()
    {
        // Simulates: click the name span → type a new name → press Enter → new name is shown.
        await NavigateAsync(BuilderUrl);
        await WaitForToolbarAsync();

        // Click the display span — WorkflowToolbar.razor replaces it with an <input> for inline editing.
        await Page.ClickAsync("span[aria-label='Rename workflow — click to edit']");

        var nameInput = Page.Locator("input[aria-label='Edit workflow name']");
        await nameInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        await nameInput.FillAsync("My Renamed Workflow");
        await nameInput.PressAsync("Enter");

        // After Enter, the input collapses back to the display span with the committed name.
        var nameSpan = Page.Locator("span[aria-label='Rename workflow — click to edit']");
        await nameSpan.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        Assert.Equal("My Renamed Workflow", (await nameSpan.InnerTextAsync()).Trim());
    }

    [Fact]
    public async Task PaletteSearch_TypeKeyword_FiltersVisibleNodes()
    {
        // Simulates: type in the palette search box and confirm the list narrows to matching nodes.
        await NavigateAsync(BuilderUrl);
        await WaitForToolbarAsync();

        // WorkflowNodePalette.razor debounces search input at 100 ms.
        // Palette node labels: "Start / Trigger", "Reason & Decide", "Smart Branch", "Notify", etc.
        var searchBox = Page.Locator("input[placeholder='Search nodes…']");
        await searchBox.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await searchBox.FillAsync("trigger");
        await Page.WaitForTimeoutAsync(250);

        // The "Start / Trigger" entry must remain visible after filtering.
        var triggerButton = Page.Locator("button[aria-label='Add Start / Trigger node to canvas']");
        await triggerButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });

        // "Notify" has no match for "trigger" in label, subtitle, tooltip, or search tags.
        var notifyButton = Page.Locator("button[aria-label='Add Notify node to canvas']");
        Assert.False(await notifyButton.IsVisibleAsync(),
            "Non-matching palette nodes should be hidden by the search filter.");
    }

    [Fact]
    public async Task PaletteClickToPlace_AddsNewNodeToCanvas()
    {
        // Simulates: click an "Add … to canvas" palette button and confirm a new node tile appears.
        await NavigateAsync(BuilderUrl);
        await WaitForToolbarAsync();

        // Ensure the canvas container has actual layout height before counting nodes.
        // Blazor Server renders nodes in OnAfterRenderAsync, which may run after the toolbar
        // becomes visible, so we read the baseline count after the canvas is painted.
        await Page.WaitForFunctionAsync(@"
            () => {
                const el = document.getElementById('workflow-canvas-drop-zone');
                return el !== null && el.getBoundingClientRect().height > 0;
            }");
        var nodeCountBefore = await Page.Locator(".workflow-node").CountAsync();

        // Click "Add Reason & Decide node to canvas" — multiple AgenticReason nodes can be placed freely.
        // Avoid the Trigger button: the example workflow already has one, so clicking it shows a toast
        // (PlaceNodeDefaultPositionAsync guards against duplicate triggers) without adding a node.
        var addButton = Page.Locator("button[aria-label='Add Reason & Decide node to canvas']");
        await addButton.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await addButton.ClickAsync();

        // A new .workflow-node element must appear in the diagram.
        // 10 s timeout covers multi-cycle Blazor re-renders on slower machines.
        await Page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('.workflow-node').length > {nodeCountBefore}",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        Assert.True(await Page.Locator(".workflow-node").CountAsync() > nodeCountBefore,
            "A new .workflow-node tile must appear after a palette click-to-place.");
    }

    [Fact]
    public async Task RunButton_Click_OpensRunInputModal()
    {
        // Simulates: click Run → fill scenario → cancel. Proves the run modal renders and accepts input.
        await NavigateAsync(BuilderUrl);
        await WaitForToolbarAsync();

        // Example workflow has CanRun=true — the Run button carries class "run-btn-ready".
        var runButton = Page.Locator(".run-btn-ready");
        await runButton.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await runButton.ClickAsync();

        // WorkflowRunInputModal.razor renders role="dialog" aria-modal="true" with h2 "Run Workflow".
        var modal = Page.Locator("div[role='dialog'][aria-modal='true']");
        await modal.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        await Assertions.Expect(modal.Locator("h2")).ToHaveTextAsync("Run Workflow");

        // The scenario textarea must be present and accept text input.
        var scenarioTextarea = modal.Locator("textarea#scenario-input");
        await scenarioTextarea.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await scenarioTextarea.FillAsync("A P1 outage from a customer whose database is down.");

        // Cancel closes the modal without triggering any run.
        await modal.Locator("button[aria-label='Cancel run']").ClickAsync();
        await modal.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 5_000 });
    }

    [Fact]
    public async Task ChatToggleButton_Toggles_ChatPanel()
    {
        // The code assistant now lives in the shared shell rail, which is open by default (spec-014 US4),
        // so the chat panel is visible on load. The toolbar toggle must close it and reopen it.
        await NavigateAsync(BuilderUrl);
        await WaitForToolbarAsync();

        var chatPanel = Page.Locator("aside.workflow-chat-panel");
        await chatPanel.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });

        // Close: the toggle reads "Close chat panel" while open.
        await Page.ClickAsync("button[aria-label='Close chat panel']");
        await chatPanel.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 5_000 });

        // Reopen: the toggle now reads "Open chat panel".
        await Page.ClickAsync("button[aria-label='Open chat panel']");
        await chatPanel.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
    }

    [Fact]
    public async Task RootBuilderUrl_Loads_WithoutDatabaseError()
    {
        // Navigate to /workflow-builder (no id) — triggers ListByOwnerAsync → queries WorkflowDefinitions.
        // Catches the "no such table: Workflows" regression (EF Core name vs raw SQL migration name).
        await NavigateAsync("/workflow-builder");

        var bodyText = await Page.InnerTextAsync("body");
        Assert.DoesNotContain("unhandled exception", bodyText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no such table", bodyText, StringComparison.OrdinalIgnoreCase);

        // Either the entry-choice modal or the canvas shell must be in the DOM — not an error page.
        var shell = Page.Locator("div.workflow-builder-shell");
        await shell.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Waits for the toolbar to be visible, confirming Blazor's first interactive render pass
    /// completed over SignalR. Must be called before any interaction test proceeds.
    /// </summary>
    private async Task WaitForToolbarAsync()
    {
        await Page.Locator(".workflow-toolbar").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
    }
}
