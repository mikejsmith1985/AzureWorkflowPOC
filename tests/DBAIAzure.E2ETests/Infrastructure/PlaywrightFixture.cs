// Manages the Playwright browser instance shared across all E2E tests.
// Each test gets its own BrowserContext (isolated cookies/session) but shares one Browser
// instance to reduce startup overhead.
using Microsoft.Playwright;

namespace DBAIAzure.E2ETests.Infrastructure;

/// <summary>
/// xUnit collection fixture that initialises a headless Chromium browser for the duration
/// of the E2E test run and exposes a factory for per-test browser contexts.
/// </summary>
public sealed class PlaywrightFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser?    _browser;

    public IBrowser Browser => _browser
        ?? throw new InvalidOperationException("Browser is not initialised — InitializeAsync has not run.");

    public async Task InitializeAsync()
    {
        // Ensure Playwright browser binaries are installed (no-op if already present).
        Microsoft.Playwright.Program.Main(["install", "chromium"]);

        _playwright = await Playwright.CreateAsync();
        _browser    = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            // Slow down interactions by 50 ms so flaky timing issues surface in CI.
            SlowMo   = 50,
        });
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null) await _browser.CloseAsync();
        _playwright?.Dispose();
    }

    /// <summary>
    /// Creates a fresh, isolated browser context for one test.
    /// Caller is responsible for disposing the returned context.
    /// </summary>
    public Task<IBrowserContext> NewContextAsync() =>
        Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL           = WebAppFixture.BaseUrl,
            IgnoreHTTPSErrors = true,
        });
}
