// E2E coverage for the shell-wide Assistant rail (feature 014, US4): static chrome on every screen,
// collapse/reopen with content reflow, open/closed persistence across reload, and — on the Workflow
// Builder — the existing code-assistant chat hosted in the same rail. Run via scripts/run-e2e.ps1.
using DBAIAzure.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace DBAIAzure.E2ETests.Tests;

/// <summary>
/// Verifies the Assistant panel chrome (header, intro, suggestion chips, input) appears on a
/// non-Builder screen, collapses and reopens with the content reclaiming the width, persists its
/// open/closed state across a reload, and hosts the working chat panel on the Builder (C-AP-1..4).
/// </summary>
public sealed class AssistantPanelTests : E2ETestBase
{
    public AssistantPanelTests(WebAppFixture webApp, PlaywrightFixture playwright)
        : base(webApp, playwright) { }

    [Fact]
    public async Task AssistantChrome_IsPresent_OnNonBuilderScreen()
    {
        // The Review Queue is a non-Builder destination, so the rail shows the static chrome.
        await NavigateAsync("/review-queue");

        await Assertions.Expect(Page.Locator("[data-testid='assistant-panel']")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("[data-testid='assistant-intro']")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("[data-testid='assistant-suggestion']").First).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("[data-testid='assistant-input']")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Assistant_Collapses_AndReopens()
    {
        await NavigateAsync("/review-queue");

        // Collapse: the panel body hides and a compact expand affordance appears.
        await Page.Locator("[data-testid='assistant-collapse']").ClickAsync();
        await Assertions.Expect(Page.Locator("[data-testid='assistant-panel']")).ToBeHiddenAsync();
        await Assertions.Expect(Page.Locator("[data-testid='assistant-expand']")).ToBeVisibleAsync();

        // Reopen: the panel body returns.
        await Page.Locator("[data-testid='assistant-expand']").ClickAsync();
        await Assertions.Expect(Page.Locator("[data-testid='assistant-panel']")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Assistant_CollapsedState_PersistsAcrossReload()
    {
        await NavigateAsync("/review-queue");

        await Page.Locator("[data-testid='assistant-collapse']").ClickAsync();
        await Assertions.Expect(Page.Locator("[data-testid='assistant-expand']")).ToBeVisibleAsync();

        // The choice lives in localStorage, so a fresh load must restore the collapsed rail.
        await Page.ReloadAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Assertions.Expect(Page.Locator("[data-testid='assistant-expand']")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("[data-testid='assistant-panel']")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Builder_HostsWorkingChatPanel_InRail()
    {
        await NavigateAsync("/workflow-builder");

        // The Builder toolbar's Chat toggle opens the shell rail, which hosts the existing
        // code-assistant chat (aria-label preserved from WorkflowChatPanel).
        var chatPanel = Page.Locator("[aria-label='AI code assistant panel']");
        if (await chatPanel.CountAsync() == 0)
        {
            await Page.GetByTitle("AI Chat").ClickAsync();
        }

        await Assertions.Expect(chatPanel).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByLabel("Chat input — type a message or refinement request")).ToBeVisibleAsync();
    }
}
