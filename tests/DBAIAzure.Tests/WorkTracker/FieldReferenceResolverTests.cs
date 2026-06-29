// Unit tests for AdoFieldReferenceResolver — logical names map to Custom.*; qualified refs pass through.
using DBAIAzure.Web.Integrations.AzureDevOps;
using Xunit;

namespace DBAIAzure.Tests.WorkTracker;

public sealed class FieldReferenceResolverTests
{
    private readonly AdoFieldReferenceResolver _resolver = new();

    [Theory]
    [InlineData("AIRuntimeCostUSD", "Custom.AIRuntimeCostUSD")]
    [InlineData("CostBindingKey", "Custom.CostBindingKey")]
    public void ToNative_PrefixesLogicalName(string logical, string expected)
    {
        Assert.Equal(expected, _resolver.ToNativeReference(logical));
    }

    [Theory]
    [InlineData("Custom.AIDevCostUSD")]   // already a Custom.* reference
    [InlineData("System.Tags")]           // system reference
    public void ToNative_PassesQualifiedReferencesThrough(string qualified)
    {
        Assert.Equal(qualified, _resolver.ToNativeReference(qualified));
    }
}
