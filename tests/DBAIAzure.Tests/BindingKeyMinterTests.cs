// Unit tests for BindingKeyMinter — branch-safe, unique keys; validation rejects unusable input.
using DBAIAzure.Web.Services;
using Xunit;

namespace DBAIAzure.Tests;

public sealed class BindingKeyMinterTests
{
    private readonly BindingKeyMinter _minter = new();

    [Fact]
    public void Mint_ProducesBranchSafeUniqueKeys()
    {
        var a = _minter.Mint();
        var b = _minter.Mint();

        Assert.NotEqual(a, b);
        Assert.StartsWith("BIND-", a);
        Assert.All(a, c => Assert.True(char.IsLetterOrDigit(c) || c == '-'));   // branch-safe
        Assert.True(_minter.IsValid(a));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab/cd")]        // slash — not branch-safe
    [InlineData("has space")]    // whitespace
    [InlineData("ab")]           // too short
    public void IsValid_RejectsUnusableInput(string? candidate)
    {
        Assert.False(_minter.IsValid(candidate));
    }
}
