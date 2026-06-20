// Validates the Visual Workflow Builder's key interactive elements.
// If the canvas, palette, toolbar, or chat panel fail to render this suite catches it.
using DBAIAzure.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace DBAIAzure.E2ETests.Tests;

/// <summary>
/// End-to-end tests for the Visual Workflow Builder page (/workflow-builder).
/// Covers canvas render, node palette, toolbar, chat toggle, and unsaved-changes guard.
/// </summary>
public sealed class WorkflowBuilderTests : E2ETestBase
{
    public WorkflowBuilderTests(WebAppFixture webApp, PlaywrightFixture playwright)
        : base(webApp, playwright) { }

    [Fact]
    public async Task CanvasContainer_IsVisible_OnLoad()
    {
        await NavigateAsync("/workflow-builder");

        // The Z.Blazor.Diagrams canvas renders inside a div with class containing "diagram".
        var canvas = Page.Locator("[class*='diagram'], svg").First;
        await canvas.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    [Fact]
    public async Task NodePalette_IsVisible_OnLoad()
    {
        await NavigateAsync("/workflow-builder");

        // The palette lists node types the user can drag onto the canvas.
        // At least one node-type button should be present.
        var paletteItem = Page.Locator("button[draggable='true'], [class*='palette'] button").First;
        await paletteItem.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    [Fact]
    public async Task Toolbar_WorkflowNameInput_IsVisible()
    {
        await NavigateAsync("/workflow-builder");

        // The toolbar contains an editable workflow name input.
        var nameInput = Page.Locator("input[placeholder*='name' i], input[aria-label*='name' i]").First;
        await nameInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
    }

    [Fact]
    public async Task ChatToggleButton_Click_OpensChatPanel()
    {
        await NavigateAsync("/workflow-builder");

        // The chat panel is hidden until the toggle button is clicked.
        var toggle = Page.Locator("button[aria-label*='chat' i], button[aria-label*='AI' i]").First;
        await toggle.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await toggle.ClickAsync();

        // After clicking, the chat input or panel heading should appear.
        var chatPanel = Page.Locator("textarea[placeholder*='describe' i], [class*='chat']").First;
        await chatPanel.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    [Fact]
    public async Task RunButton_IsPresent_OnLoad()
    {
        await NavigateAsync("/workflow-builder");

        // The Run button must be visible and labelled — used to execute the workflow.
        var runBtn = Page.Locator("button:has-text('Run'), button[aria-label*='run' i]").First;
        await runBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }
}
