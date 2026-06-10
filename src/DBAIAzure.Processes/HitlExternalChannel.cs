// Bridges the SK Process Framework's proxy event to the runner's HITL collection loop.
// When HitlPauseStep emits AwaitHuman, the process routes it through the proxy step,
// which calls this channel. The runner inspects WasPaused after RunToEndAsync returns,
// then collects Console.ReadLine() before restarting the process with HumanResponded.
using Microsoft.SemanticKernel;

namespace DBAIAzure.Processes;

public sealed class HitlExternalChannel : IExternalKernelProcessMessageChannel
{
    private KernelProcessProxyMessage? _pausedMessage;

    /// <summary>True once the process has emitted an AwaitHuman external event.</summary>
    public bool WasPaused => _pausedMessage is not null;

    /// <summary>The proxy message received from the process; null until WasPaused is true.</summary>
    public KernelProcessProxyMessage? PausedMessage => _pausedMessage;

    public ValueTask Initialize() => ValueTask.CompletedTask;

    public ValueTask Uninitialize() => ValueTask.CompletedTask;

    public Task EmitExternalEventAsync(string externalTopicEvent, KernelProcessProxyMessage message)
    {
        if (externalTopicEvent == Events.AwaitHuman)
            _pausedMessage = message;
        return Task.CompletedTask;
    }
}
