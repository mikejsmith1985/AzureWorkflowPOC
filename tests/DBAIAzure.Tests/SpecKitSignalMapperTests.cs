// Unit tests for SpecKitSignalMapper — the minted cost binding key (spec-017) and the triggered-by
// identity (#44) both flow from intake/signal into the phase run's initial state.
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

    [Fact]
    public void ToInitialState_MapsTriggeredBy_FromSignal()
    {
        var payload = new PhaseSignalPayload
        {
            FeatureKey = "016-llm-telemetry-capture",
            Phase = "plan",
            TriggeredBy = "dev@example.com",
        };

        var state = SpecKitSignalMapper.ToInitialState(payload, "run1", "specs", "BIND-X");

        Assert.Equal("dev@example.com", state.TriggeredBy);
    }

    [Fact]
    public void ToInitialState_BlankTriggeredBy_IsNull()
    {
        var payload = new PhaseSignalPayload { FeatureKey = "016-x", Phase = "plan", TriggeredBy = "  " };

        var state = SpecKitSignalMapper.ToInitialState(payload, "run1", "specs", "BIND-X");

        Assert.Null(state.TriggeredBy);
    }
}
