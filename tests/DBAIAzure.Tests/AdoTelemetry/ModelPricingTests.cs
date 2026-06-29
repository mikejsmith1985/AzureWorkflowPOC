// Unit tests for ModelPricing.EstimateCostUsd — per-tier rates, unknown models, blank model names.
using DBAIAzure.Core.Models.AdoTelemetry;
using Xunit;

namespace DBAIAzure.Tests.AdoTelemetry;

public sealed class ModelPricingTests
{
    [Fact]
    public void EstimateCostUsd_SonnetTier_UsesSonnetRates()
    {
        // 1M input @ $3 + 1M output @ $15 = $18.00
        var cost = ModelPricing.EstimateCostUsd("claude-sonnet-4-6", 1_000_000, 1_000_000);

        Assert.Equal(18.0, cost);
    }

    [Fact]
    public void EstimateCostUsd_OpusTier_UsesOpusRates()
    {
        // 1M input @ $15 + 0 output = $15.00
        var cost = ModelPricing.EstimateCostUsd("claude-opus-4-8", 1_000_000, 0);

        Assert.Equal(15.0, cost);
    }

    [Fact]
    public void EstimateCostUsd_SmallUsage_RoundsToFourPlaces()
    {
        // 1000 input @ $3/M + 500 output @ $15/M = 0.003 + 0.0075 = 0.0105
        var cost = ModelPricing.EstimateCostUsd("claude-sonnet-4-6", 1_000, 500);

        Assert.Equal(0.0105, cost);
    }

    [Theory]
    [InlineData("gpt-4o")]
    [InlineData("some-unknown-model")]
    [InlineData("")]
    [InlineData(null)]
    public void EstimateCostUsd_UnknownOrBlankModel_ReturnsNull(string? modelName)
    {
        var cost = ModelPricing.EstimateCostUsd(modelName, 1_000, 1_000);

        Assert.Null(cost);
    }
}
