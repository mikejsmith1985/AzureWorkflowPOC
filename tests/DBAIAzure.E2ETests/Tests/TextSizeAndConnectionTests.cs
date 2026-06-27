// E2E coverage for the top-bar controls (feature 014, US5): the text-size control rescales content
// and the choice survives a reload, and the connection indicator shows the connected state plus the
// current host (FR-018/019/020). Run via scripts/run-e2e.ps1.
using DBAIAzure.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace DBAIAzure.E2ETests.Tests;

/// <summary>
/// Verifies the text-size control drives the root <c>--text-scale</c> token and persists across a
/// reload, and the connection indicator reports the connected state and names the host (FR-018..020).
/// </summary>
public sealed class TextSizeAndConnectionTests : E2ETestBase
{
    public TextSizeAndConnectionTests(WebAppFixture webApp, PlaywrightFixture playwright)
        : base(webApp, playwright) { }

    // Polls the document root until --text-scale reaches the expected multiplier (throws on timeout).
    private Task WaitForTextScaleAsync(string expected) =>
        Page.WaitForFunctionAsync(
            "expected => getComputedStyle(document.documentElement).getPropertyValue('--text-scale').trim() === expected",
            expected);

    [Fact]
    public async Task TextSize_Large_RescalesContent()
    {
        await NavigateAsync("/review-queue");

        await Page.Locator("[data-testid='text-size-large']").ClickAsync();

        await WaitForTextScaleAsync("1.15");
    }

    [Fact]
    public async Task TextSize_Choice_SurvivesReload()
    {
        await NavigateAsync("/review-queue");

        await Page.Locator("[data-testid='text-size-large']").ClickAsync();
        await WaitForTextScaleAsync("1.15");

        await Page.ReloadAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The stored choice must be re-applied to the document root after the fresh load.
        await WaitForTextScaleAsync("1.15");
    }

    [Fact]
    public async Task ConnectionIndicator_ShowsConnected_AndHost()
    {
        await NavigateAsync("/review-queue");

        var indicator = Page.Locator("[data-testid='connection-indicator']");
        await Assertions.Expect(indicator).ToBeVisibleAsync();
        await Assertions.Expect(indicator).ToHaveAttributeAsync("data-connected", "true");
        // The indicator names the host it is connected to (host:port of the test server).
        await Assertions.Expect(indicator).ToContainTextAsync("localhost");
    }
}
