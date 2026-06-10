using DBAIAzure.Processes;
using Microsoft.SemanticKernel;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Unit tests for HitlExternalChannel — the bridge between the SK Process Framework
/// proxy step and the runner's HITL input collection loop.
/// </summary>
public class HitlExternalChannelTests
{
    // ── Initial state ──────────────────────────────────────────────────────────

    [Fact]
    public void WasPaused_IsFalse_BeforeAnyEvent()
    {
        var channel = new HitlExternalChannel();

        Assert.False(channel.WasPaused);
        Assert.Null(channel.PausedMessage);
    }

    // ── AwaitHuman topic triggers the pause ───────────────────────────────────

    [Fact]
    public async Task EmitExternalEventAsync_AwaitHumanTopic_SetsPausedMessageAsync()
    {
        var channel = new HitlExternalChannel();
        var proxyMessage = new KernelProcessProxyMessage();

        await channel.EmitExternalEventAsync(Events.AwaitHuman, proxyMessage);

        Assert.True(channel.WasPaused);
        Assert.Same(proxyMessage, channel.PausedMessage);
    }

    // ── Unrelated topics are ignored ──────────────────────────────────────────

    [Fact]
    public async Task EmitExternalEventAsync_UnknownTopic_DoesNotSetPausedMessageAsync()
    {
        var channel = new HitlExternalChannel();
        var proxyMessage = new KernelProcessProxyMessage();

        await channel.EmitExternalEventAsync("SomeOtherTopic", proxyMessage);

        Assert.False(channel.WasPaused);
        Assert.Null(channel.PausedMessage);
    }

    // ── Lifecycle methods complete without error ───────────────────────────────

    [Fact]
    public async Task Initialize_And_Uninitialize_CompleteSuccessfullyAsync()
    {
        var channel = new HitlExternalChannel();

        await channel.Initialize();
        await channel.Uninitialize();
    }
}
