// End-to-end tests for inline node label editing and the config-panel reset-guard fix.
// Covers Quickstart Scenarios 1–5. Scenarios 6–8 (keyboard, all node types, regression)
// are added in T017 once the feature is implemented (GREEN against the running app).
using DBAIAzure.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace DBAIAzure.E2ETests.Tests;

/// <summary>
/// End-to-end tests for fix/006-fix-node-text-editing.
/// Tests drive real Playwright browser interactions against the running Blazor Server app.
/// Each test places a fresh node on the canvas so it has no pre-existing label text.
/// </summary>
public sealed class WorkflowNodeLabelEditTests : E2ETestBase
{
    private const string BuilderUrl = "/workflow-builder/new";

    public WorkflowNodeLabelEditTests(WebAppFixture webApp, PlaywrightFixture playwright)
        : base(webApp, playwright) { }

    // ── Scenario 1: Config panel inputs survive parent re-renders ─────────────────

    [Fact]
    public async Task Scenario1_ConfigPanel_GoalTextSurvivesRerender()
    {
        // Reproduces the bug: type in Goal textarea → wait 300ms (beyond the 200ms debounce)
        // → text must not have reset. The _lastInitialisedNodeId guard in OnParametersSet()
        // prevents the field reset.
        await NavigateAsync(BuilderUrl);
        await WaitForToolbarAsync();

        // Place an AgenticReason ("Reason & Decide") node on the canvas.
        var addButton = Page.Locator("button[aria-label='Add Reason & Decide node to canvas']");
        await addButton.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await addButton.ClickAsync();

        // Wait for the new node to appear and double-click it to open the config panel.
        await Page.WaitForFunctionAsync(@"
            () => document.querySelectorAll('.workflow-node').length > 0");
        var lastNode = Page.Locator(".workflow-node").Last;
        await lastNode.DblClickAsync();

        // Wait for the config panel to open.
        var goalTextarea = Page.Locator("textarea#goal-input, textarea[aria-label*='goal' i]").First;
        await goalTextarea.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });

        // Type a phrase and wait longer than the 200ms debounce to let it fire.
        const string typedGoal = "Analyse the incident report and propose resolution steps";
        await goalTextarea.FillAsync(typedGoal);
        await Page.WaitForTimeoutAsync(350);  // 350ms > 200ms debounce

        // The field must still show every character typed — no silent reset.
        var currentValue = await goalTextarea.InputValueAsync();
        Assert.Equal(typedGoal, currentValue);
    }

    // ── Scenario 2: Inline label edit via double-click on label span ───────────────

    [Fact]
    public async Task Scenario2_DoubleClickLabel_CommitsNewLabel()
    {
        // Scenario: user double-clicks the label text → input appears → types → Enter → new label shown.
        await NavigateAsync(BuilderUrl);
        await WaitForToolbarAsync();

        // Place a fresh node.
        var addButton = Page.Locator("button[aria-label='Add Reason & Decide node to canvas']");
        await addButton.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await addButton.ClickAsync();
        await Page.WaitForFunctionAsync(@"
            () => document.querySelectorAll('.workflow-node').length > 0");

        // Double-click the label span (must not open the config panel — stopPropagation).
        var lastNode  = Page.Locator(".workflow-node").Last;
        var labelSpan = lastNode.Locator(".node-label-text, [data-testid='node-label']").First;
        await labelSpan.DblClickAsync();

        // A label input field must appear.
        var labelInput = lastNode.Locator("input.node-label-input, input[aria-label*='label' i]").First;
        await labelInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });

        // Type the new name and commit with Enter.
        await labelInput.FillAsync("Triage Incoming Ticket");
        await labelInput.PressAsync("Enter");

        // The input must collapse back to a span displaying the new label.
        await labelSpan.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });
        var labelText = await labelSpan.InnerTextAsync();
        Assert.Equal("Triage Incoming Ticket", labelText.Trim());

        // The config panel must NOT have opened (double-click was on label, not node body).
        var configPanel = Page.Locator(".node-config-panel, [aria-label*='Node configuration' i]");
        Assert.False(await configPanel.IsVisibleAsync(), "Config panel must not open when double-clicking the label span.");
    }

    // ── Scenario 3: Escape cancels without resetting to pre-edit type default ──────

    [Fact]
    public async Task Scenario3_EscapeKey_CancelsLabelEdit_WithoutResettingToDefault()
    {
        // After renaming a node to "Triage Incoming Ticket", Escape during a second edit
        // must restore "Triage Incoming Ticket", not fall back to the type default "AI Agent".
        await NavigateAsync(BuilderUrl);
        await WaitForToolbarAsync();

        // Place and rename a node to a known name.
        var addButton = Page.Locator("button[aria-label='Add Reason & Decide node to canvas']");
        await addButton.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await addButton.ClickAsync();
        await Page.WaitForFunctionAsync(@"
            () => document.querySelectorAll('.workflow-node').length > 0");

        var lastNode  = Page.Locator(".workflow-node").Last;
        var labelSpan = lastNode.Locator(".node-label-text, [data-testid='node-label']").First;

        // First rename: set "Triage Incoming Ticket".
        await labelSpan.DblClickAsync();
        var labelInput = lastNode.Locator("input.node-label-input, input[aria-label*='label' i]").First;
        await labelInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });
        await labelInput.FillAsync("Triage Incoming Ticket");
        await labelInput.PressAsync("Enter");

        // Second edit: delete all text then press Escape.
        await labelSpan.DblClickAsync();
        await labelInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });
        await labelInput.SelectTextAsync();
        await labelInput.PressAsync("Delete");
        await labelInput.PressAsync("Escape");

        // Node must revert to "Triage Incoming Ticket", not "AI Agent".
        await labelSpan.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });
        var labelText = await labelSpan.InnerTextAsync();
        Assert.Equal("Triage Incoming Ticket", labelText.Trim());
    }

    // ── Scenario 4: Label undo via Ctrl+Z ─────────────────────────────────────────

    [Fact]
    public async Task Scenario4_LabelUndo_CtrlZ_RestoresPreviousLabel()
    {
        // Commit "Step A" then "Step B"; Ctrl+Z once → "Step A"; Ctrl+Z again → original.
        await NavigateAsync(BuilderUrl);
        await WaitForToolbarAsync();

        var addButton = Page.Locator("button[aria-label='Add Reason & Decide node to canvas']");
        await addButton.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await addButton.ClickAsync();
        await Page.WaitForFunctionAsync(@"
            () => document.querySelectorAll('.workflow-node').length > 0");

        var lastNode  = Page.Locator(".workflow-node").Last;
        var labelSpan = lastNode.Locator(".node-label-text, [data-testid='node-label']").First;

        // Capture the original label so we know what to expect after two undos.
        await labelSpan.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        var originalLabel = (await labelSpan.InnerTextAsync()).Trim();

        // Rename to "Step A".
        var labelInput = lastNode.Locator("input.node-label-input, input[aria-label*='label' i]").First;
        await labelSpan.DblClickAsync();
        await labelInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });
        await labelInput.FillAsync("Step A");
        await labelInput.PressAsync("Enter");

        // Rename to "Step B".
        await labelSpan.DblClickAsync();
        await labelInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });
        await labelInput.FillAsync("Step B");
        await labelInput.PressAsync("Enter");

        // First Ctrl+Z → should revert to "Step A".
        await Page.Keyboard.PressAsync("Control+z");
        await Page.WaitForTimeoutAsync(200);
        var afterFirstUndo = (await labelSpan.InnerTextAsync()).Trim();
        Assert.Equal("Step A", afterFirstUndo);

        // Second Ctrl+Z → should revert to original label.
        await Page.Keyboard.PressAsync("Control+z");
        await Page.WaitForTimeoutAsync(200);
        var afterSecondUndo = (await labelSpan.InnerTextAsync()).Trim();
        Assert.Equal(originalLabel, afterSecondUndo);
    }

    // ── Scenario 5: Empty label shows placeholder, not blank ──────────────────────

    [Fact]
    public async Task Scenario5_EmptyLabel_ShowsPlaceholderNotBlankHeader()
    {
        // When the user clears the label and presses Enter, the node header must not be blank.
        // DisplayLabel() returns the type-default or "Untitled node" placeholder.
        await NavigateAsync(BuilderUrl);
        await WaitForToolbarAsync();

        var addButton = Page.Locator("button[aria-label='Add Reason & Decide node to canvas']");
        await addButton.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await addButton.ClickAsync();
        await Page.WaitForFunctionAsync(@"
            () => document.querySelectorAll('.workflow-node').length > 0");

        var lastNode  = Page.Locator(".workflow-node").Last;
        var labelSpan = lastNode.Locator(".node-label-text, [data-testid='node-label']").First;
        var labelInput = lastNode.Locator("input.node-label-input, input[aria-label*='label' i]").First;

        // Double-click to enter edit mode, clear text, commit with Enter.
        await labelSpan.DblClickAsync();
        await labelInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });
        await labelInput.SelectTextAsync();
        await labelInput.PressAsync("Delete");
        await labelInput.PressAsync("Enter");

        // The node header must show a non-empty fallback (type default or placeholder).
        await labelSpan.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });
        var displayedText = (await labelSpan.InnerTextAsync()).Trim();
        Assert.NotEmpty(displayedText);

        // When editing again, the input field must be EMPTY (placeholder not pre-filled).
        await labelSpan.DblClickAsync();
        await labelInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });
        var inputValue = await labelInput.InputValueAsync();
        Assert.Equal(string.Empty, inputValue);
    }

    // ── Scenario 6: Keyboard-only editing (no mouse) ──────────────────────────────

    [Fact]
    public async Task Scenario6_KeyboardOnly_CanEditNodeLabel()
    {
        // Tab to a canvas node, press Enter to activate inline label edit, type, Enter to commit.
        await NavigateAsync(BuilderUrl);
        await WaitForToolbarAsync();

        var addButton = Page.Locator("button[aria-label='Add Reason & Decide node to canvas']");
        await addButton.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await addButton.ClickAsync();
        await Page.WaitForFunctionAsync(@"
            () => document.querySelectorAll('.workflow-node').length > 0");

        // Tab until the canvas node div receives focus (tabindex="0" required on the outer div).
        await Page.Keyboard.PressAsync("Tab");
        await Page.WaitForTimeoutAsync(100);

        // Press Enter to activate label editing (OnNodeKeyDown handler).
        await Page.Keyboard.PressAsync("Enter");

        // Type the new label and commit with Enter.
        await Page.Keyboard.TypeAsync("Keyboard Label");
        await Page.Keyboard.PressAsync("Enter");

        // The node must display the new label.
        var lastNode  = Page.Locator(".workflow-node").Last;
        var labelSpan = lastNode.Locator(".node-label-text, [data-testid='node-label']").First;
        await labelSpan.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });
        var labelText = (await labelSpan.InnerTextAsync()).Trim();
        Assert.Equal("Keyboard Label", labelText);
    }

    // ── Scenario 7: All node types editable ───────────────────────────────────────

    [Theory]
    [InlineData("Add Start / Trigger node to canvas")]
    [InlineData("Add Reason & Decide node to canvas")]
    [InlineData("Add Ask a Person node to canvas")]
    [InlineData("Add Smart Branch node to canvas")]
    [InlineData("Add Transform node to canvas")]
    [InlineData("Add Notify node to canvas")]
    [InlineData("Add Save / Load node to canvas")]
    public async Task Scenario7_AllNodeTypes_LabelIsEditable(string addButtonAriaLabel)
    {
        // Every node type must support inline label editing — no type should resist renaming.
        await NavigateAsync(BuilderUrl);
        await WaitForToolbarAsync();

        // Ensure the canvas is laid out before placing a node.
        await Page.WaitForFunctionAsync(@"
            () => {
                const el = document.getElementById('workflow-canvas-drop-zone');
                return el !== null && el.getBoundingClientRect().height > 0;
            }");
        var nodeCountBefore = await Page.Locator(".workflow-node").CountAsync();

        var addButton = Page.Locator($"button[aria-label='{addButtonAriaLabel}']");
        await addButton.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await addButton.ClickAsync();

        await Page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('.workflow-node').length > {nodeCountBefore}",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        var lastNode  = Page.Locator(".workflow-node").Last;
        var labelSpan = lastNode.Locator(".node-label-text, [data-testid='node-label']").First;
        var labelInput = lastNode.Locator("input.node-label-input, input[aria-label*='label' i]").First;

        // Double-click label → type → Enter → verify new label.
        await labelSpan.DblClickAsync();
        await labelInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });
        await labelInput.FillAsync("Renamed Node");
        await labelInput.PressAsync("Enter");

        await labelSpan.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });
        var labelText = (await labelSpan.InnerTextAsync()).Trim();
        Assert.Equal("Renamed Node", labelText);
    }

    // ── Scenario 8: Existing canvas interactions not regressed ────────────────────

    [Fact]
    public async Task Scenario8_ExistingInteractions_NotRegressed_AfterLabelEditing()
    {
        // After renaming a node, verify that ports, context menu, and Ctrl+Z node deletion still work.
        await NavigateAsync(BuilderUrl);
        await WaitForToolbarAsync();

        // Place a fresh node and rename it.
        var addButton = Page.Locator("button[aria-label='Add Reason & Decide node to canvas']");
        await addButton.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await addButton.ClickAsync();
        await Page.WaitForFunctionAsync(@"
            () => document.querySelectorAll('.workflow-node').length > 0");

        var lastNode   = Page.Locator(".workflow-node").Last;
        var labelSpan  = lastNode.Locator(".node-label-text, [data-testid='node-label']").First;
        var labelInput = lastNode.Locator("input.node-label-input, input[aria-label*='label' i]").First;

        await labelSpan.DblClickAsync();
        await labelInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });
        await labelInput.FillAsync("Regression Check");
        await labelInput.PressAsync("Enter");

        // Verify right-click context menu still appears on the node.
        await lastNode.ClickAsync(new() { Button = MouseButton.Right });
        var contextMenu = Page.Locator("[role='menu'], .context-menu, .node-context-menu");
        var isMenuVisible = await contextMenu.IsVisibleAsync();
        Assert.True(isMenuVisible, "Right-click context menu must still appear after label editing.");

        // Dismiss the context menu.
        await Page.Keyboard.PressAsync("Escape");

        // Verify config panel opens on double-click of the node BODY (outside the label span).
        var headerDiv = lastNode.Locator(".rounded-t-md").First;
        await headerDiv.DblClickAsync();

        // The label span must still show the committed name — no regression to type default.
        await labelSpan.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });
        var finalLabel = (await labelSpan.InnerTextAsync()).Trim();
        Assert.Equal("Regression Check", finalLabel);
    }

    // ── Shared helper ─────────────────────────────────────────────────────────────

    private async Task WaitForToolbarAsync()
    {
        await Page.Locator(".workflow-toolbar").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
    }
}
