using DBAIAzure.Core.Interfaces;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Structural tests confirming IHitlNotifier contract is consumable.
/// Full integration tests require a running Power Automate endpoint.
/// </summary>
public class IHitlNotifierTests
{
    [Fact]
    public void IHitlNotifier_InterfaceIsAccessible()
    {
        // Verify the interface exists in the expected namespace and is a proper interface.
        var interfaceType = typeof(IHitlNotifier);
        Assert.True(interfaceType.IsInterface);
    }

    [Fact]
    public void IHitlNotifier_HasExpectedNotifyAsyncMethod()
    {
        var interfaceType = typeof(IHitlNotifier);
        var notifyMethod = interfaceType.GetMethod("NotifyAsync");
        Assert.NotNull(notifyMethod);
    }
}
