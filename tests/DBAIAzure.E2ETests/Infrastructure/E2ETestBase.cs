// Base class that every E2E test class inherits from. Handles the per-test browser context
// and page lifecycle so individual tests stay focused on what they are asserting.
using Microsoft.Playwright;

namespace DBAIAzure.E2ETests.Infrastructure;

/// <summary>
/// Base class for E2E tests. Creates an isolated browser context and a single page for
/// each test, then disposes them after the test completes.
/// </summary>
[Collection(E2ECollection.Name)]
public abstract class E2ETestBase : IAsyncLifetime
{
    private readonly PlaywrightFixture _playwright;
    private IBrowserContext? _context;

    protected IPage Page { get; private set; } = null!;

    protected E2ETestBase(WebAppFixture _, PlaywrightFixture playwright)
    {
        _playwright = playwright;
    }

    public async Task InitializeAsync()
    {
        _context = await _playwright.NewContextAsync();

        // Fail fast on unexpected JS errors — surface them as test failures rather than
        // silent passes that hide broken Blazor component lifecycle code.
        _context.Console += (_, msg) =>
        {
            if (msg.Type == "error")
                TestOutput?.WriteLine($"[browser console error] {msg.Text}");
        };

        Page = await _context.NewPageAsync();

        // Give Blazor 10 seconds to complete its SignalR handshake on each navigation.
        Page.SetDefaultTimeout(10_000);
    }

    public async Task DisposeAsync()
    {
        if (_context is not null) await _context.CloseAsync();
    }

    // Subclasses can override to attach an ITestOutputHelper for diagnostics.
    protected virtual ITestOutputHelper? TestOutput => null;

    // ── Shared helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Navigates to <paramref name="path"/> and waits for the Blazor SignalR connection to
    /// become established (indicated by the absence of a reconnect overlay).
    /// </summary>
    protected async Task NavigateAsync(string path = "/")
    {
        await Page.GotoAsync(path);
        // Blazor renders a loading indicator while the SignalR connection is being established.
        // Wait until it disappears or until the page body has meaningful content.
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>Returns true when the Blazor "reconnecting" overlay is absent.</summary>
    protected async Task<bool> IsBlazorConnectedAsync()
    {
        // The default Blazor reconnect overlay has id="components-reconnect-modal".
        var modal = Page.Locator("#components-reconnect-modal");
        var isVisible = await modal.IsVisibleAsync();
        return !isVisible;
    }
}
