// End-to-end test for the Node Realization MVP (spec 007, Scenario A): from the visual builder a user
// clicks "Make it real", the assistant proposes per-node configuration, the user accepts them all with
// one confirmation, and the workflow reports a production-readiness verdict. Drives real user actions
// against the live Blazor Server app — the realization pass makes genuine LLM calls, so this test
// requires the Development Anthropic key (the WebAppFixture runs the app in Development).
using DBAIAzure.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace DBAIAzure.E2ETests.Tests;

/// <summary>
/// Scenario A: a plain-language workflow is turned into a runnable one. Navigates to the 4-node example
/// (Trigger → AI → Approval → Notify), runs "Make it real", accepts the proposals, and asserts the
/// readiness verdict surfaces and the workflow is runnable. The Notify node binds a messaging connector,
/// so the verdict may be "Not ready" when that connector is unconfigured in the test environment — the
/// test asserts a verdict was produced, not a specific value, to stay independent of connector health.
/// </summary>
public sealed class NodeRealizationTests : E2ETestBase
{
    // "new" is not a valid GUID, so the page loads the 4-node example without the entry-choice modal.
    private const string BuilderUrl = "/workflow-builder/new";

    // Realization runs one LLM call per node in sequence, so allow generous time for the panel to fill.
    private const int RealizationTimeoutMs = 120_000;

    public NodeRealizationTests(WebAppFixture webApp, PlaywrightFixture playwright)
        : base(webApp, playwright) { }

    [Fact]
    public async Task MakeItReal_ProposeAcceptAll_ProducesReadinessVerdict()
    {
        await NavigateAsync(BuilderUrl);
        await WaitForToolbarAsync();

        // 1. Trigger the realization pass.
        var makeItReal = Page.Locator("[data-testid='make-it-real']");
        await makeItReal.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        await makeItReal.ClickAsync();

        // 2. The review panel opens and at least one proposal streams in.
        var panel = Page.Locator("[data-testid='realization-panel']");
        await panel.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await Page.Locator("[data-testid='realization-proposal']").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = RealizationTimeoutMs });

        // 3. Once proposing finishes, "Accept all" becomes enabled — accept, then confirm once.
        var acceptAll = Page.Locator("[data-testid='accept-all']");
        await Assertions.Expect(acceptAll).ToBeEnabledAsync(new() { Timeout = RealizationTimeoutMs });
        await acceptAll.ClickAsync();

        var confirm = Page.Locator("[data-testid='accept-all-confirm']");
        await confirm.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        await confirm.ClickAsync();

        // 4. Readiness is evaluated and surfaced in the toolbar (either "Ready" or "Not ready").
        var readiness = Page.Locator("[data-testid='readiness-indicator']");
        await readiness.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });

        var verdict = (await readiness.InnerTextAsync()).Trim();
        Assert.True(
            verdict.Contains("Ready", StringComparison.OrdinalIgnoreCase),
            $"Expected a readiness verdict containing 'Ready'/'Not ready', got '{verdict}'.");

        // 5. After T048: Run is enabled when "Ready" and disabled when "Not ready" (Blocked connectors).
        //    The assertion adapts to the test environment's connector health so the test stays portable.
        var runButton = Page.Locator(".run-btn-ready");
        await runButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        var isNotReady = verdict.Contains("Not ready", StringComparison.OrdinalIgnoreCase);
        if (isNotReady)
            Assert.False(await runButton.IsEnabledAsync(),
                "Run must be disabled when the readiness verdict is 'Not ready'.");
        else
            Assert.True(await runButton.IsEnabledAsync(),
                "Run must be enabled when the readiness verdict is 'Ready'.");
    }

    [Fact]
    public async Task EditThenAccept_AppliesEditedProposal_AndRemovesItFromReview()
    {
        // US2 Scenario B (interactive element, Article V): a single proposal can be edited in plain
        // language and accepted, which applies it to its node and removes its card from the review list.
        await NavigateAsync(BuilderUrl);
        await WaitForToolbarAsync();

        await Page.Locator("[data-testid='make-it-real']").ClickAsync();
        await Page.Locator("[data-testid='realization-panel']")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        // Wait until proposing finishes (Accept-all enabled means every node has a proposal).
        await Assertions.Expect(Page.Locator("[data-testid='accept-all']"))
            .ToBeEnabledAsync(new() { Timeout = RealizationTimeoutMs });

        var proposalsBefore = await Page.Locator("[data-testid='realization-proposal']").CountAsync();

        // Open the inline editor on the first editable proposal, change the text, and save+accept.
        var firstEdit = Page.Locator("[data-testid='proposal-edit']").First;
        await firstEdit.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await firstEdit.ClickAsync();

        var editor = Page.Locator("[data-testid='proposal-edit-input']");
        await editor.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        await editor.FillAsync("Edited in plain language by the Scenario B test.");
        await Page.Locator("[data-testid='proposal-edit-save']").ClickAsync();

        // The edited proposal is applied to its node and removed from the review list.
        await Assertions.Expect(Page.Locator("[data-testid='realization-proposal']"))
            .ToHaveCountAsync(proposalsBefore - 1, new() { Timeout = 20_000 });
    }

    [Fact]
    public async Task RealizeNode_ViaContextMenu_OpensSingleNodePanel()
    {
        // US3 Scenario C: adding a new node to a workflow whose existing nodes are already configured,
        // then right-clicking → "Realize this node" proposes config for exactly that one node.
        // The panel must contain exactly 1 proposal card (not cards for all nodes), proving that
        // the per-node entry point never re-proposes nodes that were already realized.
        await NavigateAsync(BuilderUrl);
        await WaitForToolbarAsync();

        // 1. Add a new AI step via the palette (the 4-node example already has all nodes configured).
        await Page.Locator("[data-testid='palette-node-AgenticReason']").ClickAsync();

        // 2. The new node is unconfigured — its amber badge appears on the canvas.
        var unconfiguredBadge = Page.Locator("[data-testid='node-unconfigured-badge']");
        await unconfiguredBadge.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        // 3. Right-click the unconfigured node to open the context menu.
        var unconfiguredNode = Page.Locator(".workflow-node")
            .Filter(new() { Has = Page.Locator("[data-testid='node-unconfigured-badge']") });
        await unconfiguredNode.First.ClickAsync(new() { Button = MouseButton.Right });

        // 4. Click "Realize this node" in the context menu.
        var realizeMenuItem = Page.Locator("[data-testid='context-menu-realize-node']");
        await realizeMenuItem.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        await realizeMenuItem.ClickAsync();

        // 5. The panel opens and exactly 1 proposal arrives (only the new node, not all 5).
        await Page.Locator("[data-testid='realization-panel']")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        await Page.Locator("[data-testid='realization-proposal']").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = RealizationTimeoutMs });

        await Assertions.Expect(Page.Locator("[data-testid='realization-proposal']"))
            .ToHaveCountAsync(1, new() { Timeout = 10_000 });

        // 6. Accept the single proposal to confirm the accept path works end-to-end.
        var acceptAll = Page.Locator("[data-testid='accept-all']");
        await Assertions.Expect(acceptAll).ToBeEnabledAsync(new() { Timeout = RealizationTimeoutMs });
        await acceptAll.ClickAsync();

        await Page.Locator("[data-testid='accept-all-confirm']")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        await Page.Locator("[data-testid='accept-all-confirm']").ClickAsync();

        // 7. The readiness indicator appears after the single-node accept triggers re-evaluation.
        var readiness = Page.Locator("[data-testid='readiness-indicator']");
        await readiness.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
    }

    [Fact]
    public async Task ReadinessGate_RunButtonMatchesReadinessVerdict()
    {
        // US4 Scenario D (T049): after realization, the Run button's enabled/disabled state must
        // exactly mirror the toolbar readiness verdict. When "Not ready" (Blocked connector), Run
        // stays disabled and the toolbar shows the specific blocking reason — not the generic
        // "Set up all steps first" message. When "Ready", Run is enabled. This test is environment-
        // adaptive: it proves the gate works regardless of which connectors are installed.
        await NavigateAsync(BuilderUrl);
        await WaitForToolbarAsync();

        // Realize the example workflow and accept all proposals.
        await Page.Locator("[data-testid='make-it-real']").ClickAsync();
        await Page.Locator("[data-testid='realization-panel']")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        var acceptAll = Page.Locator("[data-testid='accept-all']");
        await Assertions.Expect(acceptAll).ToBeEnabledAsync(new() { Timeout = RealizationTimeoutMs });
        await acceptAll.ClickAsync();
        await Page.Locator("[data-testid='accept-all-confirm']")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        await Page.Locator("[data-testid='accept-all-confirm']").ClickAsync();

        // Wait for the readiness indicator then read its verdict.
        var readinessIndicator = Page.Locator("[data-testid='readiness-indicator']");
        await readinessIndicator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        var verdictText = (await readinessIndicator.InnerTextAsync()).Trim();

        var runButton = Page.Locator(".run-btn-ready");
        await runButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        var isNotReady = verdictText.Contains("Not ready", StringComparison.OrdinalIgnoreCase);
        if (isNotReady)
        {
            // T048: Run must be disabled when there are Blocked connectors.
            Assert.False(await runButton.IsEnabledAsync(),
                "Run must be disabled when the readiness report says 'Not ready'.");

            // The toolbar must show a specific blocking reason, not the generic "Set up all steps first".
            var disabledReason = Page.Locator("[data-testid='run-disabled-reason']");
            await disabledReason.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            var reasonText = (await disabledReason.InnerTextAsync()).Trim();
            Assert.NotEmpty(reasonText);
            Assert.DoesNotMatch("Set up all steps first", reasonText);
        }
        else
        {
            // All connectors are configured → Run is enabled. Proves T048 gate doesn't block unnecessarily.
            Assert.True(await runButton.IsEnabledAsync(),
                "Run must be enabled when the readiness report says 'Ready'.");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>Waits for the toolbar to be visible, confirming Blazor's first interactive render completed.</summary>
    private async Task WaitForToolbarAsync()
    {
        await Page.Locator(".workflow-toolbar").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
    }
}
