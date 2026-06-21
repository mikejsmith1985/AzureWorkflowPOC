// Starts the DBAIAzure.Web Blazor Server app as a real child process on a dedicated
// test port so Playwright can connect via a genuine HTTP/SignalR connection.
// Using a child process (not WebApplicationFactory) because Blazor Server requires real
// Kestrel + WebSocket support that TestServer does not provide.
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace DBAIAzure.E2ETests.Infrastructure;

/// <summary>
/// xUnit collection fixture that starts the web app before any E2E test runs and stops
/// it cleanly afterwards. Shared across all tests in the E2ECollection.
/// </summary>
public sealed class WebAppFixture : IAsyncLifetime
{
    // Use a non-default port so E2E tests never clash with a running dev instance.
    public const string BaseUrl = "http://localhost:5099";

    private Process? _appProcess;

    public async Task InitializeAsync()
    {
        var repoRoot = FindRepoRoot();
        var webProjectDir = Path.Combine(repoRoot, "src", "DBAIAzure.Web");

        // The project pins a user-local SDK via global.json (C:\Users\<user>\.dotnet\dotnet.exe).
        // The system dotnet at C:\Program Files\dotnet lacks ASP.NET Core 8 and will fail.
        // Mirror what start-web.ps1 does: resolve the user-profile dotnet explicitly.
        var userDotnet = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".dotnet", "dotnet.exe");
        var dotnetExe = File.Exists(userDotnet) ? userDotnet : "dotnet";

        var startInfo = new ProcessStartInfo(dotnetExe, $"run --project \"{webProjectDir}\"")
        {
            UseShellExecute       = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            WorkingDirectory       = repoRoot,
        };
        // Override the listen URL and force Development environment so the app uses
        // appsettings.Development.json (where the real Anthropic key lives).
        startInfo.EnvironmentVariables["ASPNETCORE_URLS"]        = BaseUrl;
        startInfo.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = "Development";

        _appProcess = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the web app process.");

        await WaitForReadyAsync(BaseUrl, timeoutSeconds: 60);
    }

    public async Task DisposeAsync()
    {
        if (_appProcess is { HasExited: false })
        {
            _appProcess.Kill(entireProcessTree: true);
            await _appProcess.WaitForExitAsync();
        }
        _appProcess?.Dispose();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DBAIAzure.sln")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException(
                "Cannot locate repository root — DBAIAzure.sln not found in any ancestor directory.");
    }

    private static async Task WaitForReadyAsync(string url, int timeoutSeconds)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            try
            {
                var response = await http.GetAsync(url);
                if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.OK)
                    return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SocketException)
            {
                // App not ready yet — keep polling.
            }
        }

        throw new TimeoutException(
            $"Web app did not become ready at {url} within {timeoutSeconds} seconds.");
    }
}
