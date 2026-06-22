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

        // 5. The realized workflow is runnable — every node is configured, so Run is enabled.
        var runButton = Page.Locator(".run-btn-ready");
        await runButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        Assert.True(await runButton.IsEnabledAsync(), "The Run button must be enabled once the workflow is realized.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>Waits for the toolbar to be visible, confirming Blazor's first interactive render completed.</summary>
    private async Task WaitForToolbarAsync()
    {
        await Page.Locator(".workflow-toolbar").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 20_000 });
    }
}
