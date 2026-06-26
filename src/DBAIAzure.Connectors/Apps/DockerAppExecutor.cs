// Real build/run executor using disposable Docker containers (feature 013, R1).
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Connectors.Apps;

/// <summary>Tunable options for <see cref="DockerAppExecutor"/>.</summary>
public sealed class DockerExecutorOptions
{
    /// <summary>
    /// Base image the build/run commands execute inside. Must contain the toolchain the target repo
    /// needs (node, python, dotnet…). Defaults to a small image with a POSIX shell.
    /// </summary>
    public string BaseImage { get; set; } = "alpine:3.19";

    /// <summary>Maximum number of log characters captured/persisted per operation.</summary>
    public int MaxLogChars { get; set; } = 16_000;
}

/// <summary>
/// Builds and runs an app inside fresh, disposable Docker containers via the official Docker Engine
/// API (Docker.DotNet). A build copies the bind-mounted (read-only) repo into a per-app named volume
/// and runs the build command there; a run executes the run command against that volume. Each
/// operation uses a throwaway container removed afterward by its specific id — never a wildcard
/// (Article II). Logs are captured and secret-redacted (FR-009); a timeout or a failure to start is
/// always recorded so the app is never left stuck (FR-008).
/// </summary>
public sealed class DockerAppExecutor : IAppExecutor
{
    private readonly IAppRegistryRepository _registry;
    private readonly IAppStatusNotifier _notifier;
    private readonly IDockerClient _docker;
    private readonly DockerExecutorOptions _options;

    /// <summary>Creates the executor over a Docker client and the registry/notifier seams.</summary>
    public DockerAppExecutor(
        IAppRegistryRepository registry,
        IAppStatusNotifier notifier,
        IDockerClient docker,
        DockerExecutorOptions? options = null)
    {
        _registry = registry;
        _notifier = notifier;
        _docker = docker;
        _options = options ?? new DockerExecutorOptions();
    }

    /// <inheritdoc/>
    public string ExecutorKind => "Docker";

    private static string VolumeName(string appId) => $"app013-{appId}";

    /// <inheritdoc/>
    public async Task BuildAsync(MonitoredApp app, AppExecutionRequest request, CancellationToken ct = default)
    {
        await _registry.SetStatusAsync(app.AppId, AppStatus.Building, ct);
        _notifier.NotifyChanged(app.AppId);

        var command = string.IsNullOrWhiteSpace(request.Command)
            ? BuildCommandAutoDetector.Detect(app.RepoLocalPath)
            : request.Command;

        if (string.IsNullOrWhiteSpace(command))
        {
            await RecordBuildAsync(app.AppId, false, "Could not determine a build command for this repository.", "", request.AccessToken, ct);
            return;
        }

        try
        {
            // Copy the read-only repo into the throwaway volume, then build there (never mutate the user's tree).
            var script = $"cp -a /src/. /workspace/ 2>/dev/null; cd /workspace && {command}";
            var (exitCode, logs) = await RunContainerAsync(app, request, script, mountRepo: true, ct);
            var succeeded = exitCode == 0;
            await RecordBuildAsync(app.AppId, succeeded,
                succeeded ? "Build complete." : $"Build failed (exit {exitCode}).", logs, request.AccessToken, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await RecordBuildAsync(app.AppId, false, "Build was cancelled.", "", request.AccessToken, ct);
        }
        catch (Exception ex)
        {
            // FR-008: a failure to even start the container is recorded immediately — never left "Building".
            await RecordBuildAsync(app.AppId, false, "Could not start the build container.", ex.Message, request.AccessToken, CancellationToken.None);
        }
    }

    /// <inheritdoc/>
    public async Task RunAsync(MonitoredApp app, AppExecutionRequest request, CancellationToken ct = default)
    {
        await _registry.SetStatusAsync(app.AppId, AppStatus.Running, ct);
        _notifier.NotifyChanged(app.AppId);

        try
        {
            var script = $"cd /workspace && {request.Command}";
            var (exitCode, logs, timedOut) = await RunContainerWithTimeoutAsync(app, request, script, ct);
            var outcome = timedOut ? RunOutcome.TimedOut : (exitCode == 0 ? RunOutcome.Succeeded : RunOutcome.Failed);
            var summary = outcome switch
            {
                RunOutcome.Succeeded => "exit 0",
                RunOutcome.TimedOut => $"Timed out after {request.TimeoutSeconds}s.",
                _ => $"exit {exitCode}"
            };
            await RecordRunAsync(app.AppId, outcome, summary, logs, request.AccessToken, ct);
        }
        catch (Exception ex)
        {
            await RecordRunAsync(app.AppId, RunOutcome.Failed, "Could not start the run container.", ex.Message, request.AccessToken, CancellationToken.None);
        }
    }

    // ── Container orchestration ────────────────────────────────────────────────────────────────────

    private async Task<(long exitCode, string logs)> RunContainerAsync(
        MonitoredApp app, AppExecutionRequest request, string script, bool mountRepo, CancellationToken ct)
    {
        var (exit, logs, _) = await RunContainerWithTimeoutAsync(app, request, script, ct, mountRepo);
        return (exit, logs);
    }

    private async Task<(long exitCode, string logs, bool timedOut)> RunContainerWithTimeoutAsync(
        MonitoredApp app, AppExecutionRequest request, string script, CancellationToken ct, bool mountRepo = false)
    {
        await EnsureImageAsync(_options.BaseImage, ct);
        await EnsureVolumeAsync(VolumeName(app.AppId), ct);

        var mounts = new List<Mount>
        {
            new() { Type = "volume", Source = VolumeName(app.AppId), Target = "/workspace" }
        };
        if (mountRepo)
            mounts.Add(new Mount { Type = "bind", Source = app.RepoLocalPath, Target = "/src", ReadOnly = true });

        var create = await _docker.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = _options.BaseImage,
            Cmd = new[] { "sh", "-lc", script },
            Labels = new Dictionary<string, string> { ["dbai.app013"] = app.AppId },
            HostConfig = new HostConfig { Mounts = mounts, AutoRemove = false }
        }, ct);

        var containerId = create.ID;
        var timedOut = false;
        long exitCode = -1;

        try
        {
            await _docker.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, request.TimeoutSeconds)));
            try
            {
                var wait = await _docker.Containers.WaitContainerAsync(containerId, timeoutCts.Token);
                exitCode = wait.StatusCode;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                timedOut = true;
                await SafeStopAsync(containerId);
            }

            var logs = await ReadLogsAsync(containerId);
            return (exitCode, logs, timedOut);
        }
        finally
        {
            // Throwaway: remove the specific container we created (never a wildcard — Article II).
            await SafeRemoveAsync(containerId);
        }
    }

    private async Task EnsureImageAsync(string image, CancellationToken ct)
    {
        var existing = await _docker.Images.ListImagesAsync(new ImagesListParameters
        {
            Filters = new Dictionary<string, IDictionary<string, bool>> { ["reference"] = new Dictionary<string, bool> { [image] = true } }
        }, ct);
        if (existing.Count > 0)
            return;

        await _docker.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = image },
            authConfig: null,
            new Progress<JSONMessage>(), ct);
    }

    private async Task EnsureVolumeAsync(string name, CancellationToken ct) =>
        await _docker.Volumes.CreateAsync(new VolumesCreateParameters { Name = name }, ct);

    private async Task<string> ReadLogsAsync(string containerId)
    {
        try
        {
            using var stream = await _docker.Containers.GetContainerLogsAsync(
                containerId,
                tty: false,
                new ContainerLogsParameters { ShowStdout = true, ShowStderr = true, Timestamps = false },
                CancellationToken.None);

            var (stdout, stderr) = await stream.ReadOutputToEndAsync(CancellationToken.None);
            var combined = new StringBuilder().Append(stdout).Append(stderr).ToString();
            return combined.Length > _options.MaxLogChars ? combined[^_options.MaxLogChars..] : combined;
        }
        catch (Exception ex)
        {
            return $"(could not read container logs: {ex.Message})";
        }
    }

    private async Task SafeStopAsync(string containerId)
    {
        try { await _docker.Containers.StopContainerAsync(containerId, new ContainerStopParameters { WaitBeforeKillSeconds = 5 }); }
        catch { /* best-effort — removal in finally still runs */ }
    }

    private async Task SafeRemoveAsync(string containerId)
    {
        try { await _docker.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters { Force = true }); }
        catch { /* container may already be gone */ }
    }

    private async Task RecordBuildAsync(string appId, bool succeeded, string summary, string rawLogs, string? token, CancellationToken ct)
    {
        var logs = ContainerLogRedactor.Redact(rawLogs, token);
        await _registry.SetBuildResultAsync(appId, new AppBuildResult(succeeded, summary, logs, DateTimeOffset.UtcNow), ct);
        _notifier.NotifyChanged(appId);
    }

    private async Task RecordRunAsync(string appId, RunOutcome outcome, string summary, string rawLogs, string? token, CancellationToken ct)
    {
        var logs = ContainerLogRedactor.Redact(rawLogs, token);
        await _registry.SetRunResultAsync(appId, new AppRunResult(outcome, summary, logs, DateTimeOffset.UtcNow), ct);
        _notifier.NotifyChanged(appId);
    }
}
