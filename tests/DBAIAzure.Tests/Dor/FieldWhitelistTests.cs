// Unit tests for the AI-editable field whitelist (spec-021 T034 / FR-021 / SC-006): only whitelisted keys
// survive, regardless of what the model proposed.
using DBAIAzure.Core.Models.DorWorkflow;
using Xunit;

namespace DBAIAzure.Tests.Dor;

public sealed class FieldWhitelistTests
{
    [Fact]
    public void Filter_KeepsOnlyWhitelistedKeys()
    {
        var proposed = new Dictionary<string, string>
        {
            ["description"] = "d",
            ["acceptance_criteria"] = "ac",
            ["status"] = "Done",            // not whitelisted — must be dropped
            ["assignee"] = "someone",        // not whitelisted — must be dropped
        };

        var filtered = DorFieldWhitelist.Filter(proposed, new[] { "description", "acceptance_criteria" });

        Assert.Equal(2, filtered.Count);
        Assert.True(filtered.ContainsKey("description"));
        Assert.True(filtered.ContainsKey("acceptance_criteria"));
        Assert.False(filtered.ContainsKey("status"));
        Assert.False(filtered.ContainsKey("assignee"));
    }

    [Fact]
    public void Filter_IsCaseInsensitive()
    {
        var filtered = DorFieldWhitelist.Filter(
            new Dictionary<string, string> { ["Description"] = "d" }, new[] { "description" });

        Assert.Single(filtered);
    }

    [Fact]
    public void Filter_EmptyWhitelist_DropsEverything()
    {
        var filtered = DorFieldWhitelist.Filter(
            new Dictionary<string, string> { ["description"] = "d" }, Array.Empty<string>());

        Assert.Empty(filtered);
    }
}
