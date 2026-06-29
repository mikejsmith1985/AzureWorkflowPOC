// Unit test: the minted cost binding key flows from intake into the phase run's initial state (spec-017).
using DBAIAzure.Web.Integrations.SpecKit;
using Xunit;

namespace DBAIAzure.Tests;

public sealed class SpecKitSignalMapperTests
{
    [Fact]
    public void ToInitialState_CarriesMintedBindingKey()
    {
        var payload = new PhaseSignalPayload { FeatureKey = "017-ai-cost-tracking", Phase = "plan" };

        var state = SpecKitSignalMapper.ToInitialState(payload, "run1", "specs", "BIND-ABC12345");

        Assert.Equal("BIND-ABC12345", state.CostBindingKey);
        Assert.Equal("017-ai-cost-tracking", state.FeatureKey);
    }
}
