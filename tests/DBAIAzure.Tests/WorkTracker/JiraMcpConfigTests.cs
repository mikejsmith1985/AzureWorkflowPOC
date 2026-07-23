// Unit tests for the connector-side pieces of the MCP-first Jira transport: the secret blob merge that keeps
// three Jira credentials in one row, and the per-tool argument-override box.
using System.Text.Json;
using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests.WorkTracker;

public sealed class SecretBlobMergeTests
{
    [Fact]
    public void Merge_WhenOnlyOneSecretIsTyped_KeepsTheOthersIntact()
    {
        // The regression this exists for: entering a webhook secret must not erase the stored API token.
        const string existing = """{"apiToken":"stored-token","mcpAuthToken":"stored-mcp"}""";

        var merged = SecretBlobMerge.Merge(existing, new Dictionary<string, string?>
        {
            ["apiToken"] = "",
            ["mcpAuthToken"] = "  ",
            ["jiraWebhookSecret"] = "new-webhook-secret",
        });

        using var document = JsonDocument.Parse(merged!);
        Assert.Equal("stored-token", document.RootElement.GetProperty("apiToken").GetString());
        Assert.Equal("stored-mcp", document.RootElement.GetProperty("mcpAuthToken").GetString());
        Assert.Equal("new-webhook-secret", document.RootElement.GetProperty("jiraWebhookSecret").GetString());
    }

    [Fact]
    public void Merge_WhenNothingIsTyped_ReturnsNullSoTheStoredBlobIsUntouched()
    {
        var merged = SecretBlobMerge.Merge(
            """{"apiToken":"stored"}""",
            new Dictionary<string, string?> { ["apiToken"] = null, ["jiraWebhookSecret"] = "" });

        Assert.Null(merged);
    }

    [Fact]
    public void Merge_ReplacesAValueThatWasRetyped()
    {
        var merged = SecretBlobMerge.Merge(
            """{"apiToken":"old"}""", new Dictionary<string, string?> { ["apiToken"] = "new" });

        using var document = JsonDocument.Parse(merged!);
        Assert.Equal("new", document.RootElement.GetProperty("apiToken").GetString());
    }

    [Fact]
    public void Merge_WithNoExistingBlob_WritesJustTheTypedSecrets()
    {
        var merged = SecretBlobMerge.Merge(
            null, new Dictionary<string, string?> { ["apiToken"] = "first" });

        using var document = JsonDocument.Parse(merged!);
        var only = Assert.Single(document.RootElement.EnumerateObject());
        Assert.Equal("apiToken", only.Name);
        Assert.Equal("first", only.Value.GetString());
    }

    [Fact]
    public void Merge_WithAnUnreadableExistingBlob_StillPersistsTheTypedSecrets()
    {
        var merged = SecretBlobMerge.Merge(
            "not json", new Dictionary<string, string?> { ["apiToken"] = "recovered" });

        using var document = JsonDocument.Parse(merged!);
        Assert.Equal("recovered", document.RootElement.GetProperty("apiToken").GetString());
    }
}

public sealed class JiraMcpArgumentOverridesTests
{
    [Fact]
    public void Parse_SplitsTheBlobIntoPerToolTemplates()
    {
        const string overrides = """
            {"read":{"cloudId":"abc","issueIdOrKey":"{{issueKey}}"},
             "search":{"cloudId":"abc","jql":"{{jql}}"}}
            """;

        var templates = JiraMcpArgumentOverrides.Parse(overrides);

        Assert.Contains("cloudId", templates.Read);
        Assert.Null(templates.Transition);   // role absent → the built-in default is used
        Assert.Contains("{{jql}}", templates.Search);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("""["read"]""")]   // valid JSON, wrong shape
    public void Parse_WhenBlankOrMalformed_YieldsNoOverrides(string? overrides)
    {
        var templates = JiraMcpArgumentOverrides.Parse(overrides);

        Assert.Null(templates.Read);
        Assert.Null(templates.Transition);
        Assert.Null(templates.Search);
    }

    [Fact]
    public void FormatThenParse_RoundTripsWhatTheOperatorSaved()
    {
        var original = new JiraMcpArgumentTemplates(
            """{"cloudId":"abc","issueIdOrKey":"{{issueKey}}"}""", null, """{"jql":"{{jql}}"}""");

        var reparsed = JiraMcpArgumentOverrides.Parse(JiraMcpArgumentOverrides.Format(original));

        Assert.Contains("abc", reparsed.Read);
        Assert.Null(reparsed.Transition);
        Assert.Contains("{{jql}}", reparsed.Search);
    }

    [Fact]
    public void Format_WithNoTemplates_ReturnsBlankRatherThanAnEmptyObject()
    {
        Assert.Equal(string.Empty, JiraMcpArgumentOverrides.Format(new JiraMcpArgumentTemplates(null, null, null)));
    }
}

public sealed class JiraConnectorConfigMcpTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public void JiraConnectorConfig_RoundTripsTheMcpAndTriggerFields()
    {
        var config = new JiraConnectorConfig(
            "https://acme.atlassian.net", "you@acme.com", "SBRO",
            McpServerUrl: "https://mcp.example.com/sse",
            McpReadToolName: "getJiraIssue",
            McpTransitionToolName: "transitionJiraIssue",
            McpSearchToolName: "searchJiraIssuesUsingJql",
            TriggerPollSeconds: 60);

        var restored = JsonSerializer.Deserialize<JiraConnectorConfig>(
            JsonSerializer.Serialize(config, WebJson), WebJson);

        Assert.Equal(config, restored);
    }

    [Fact]
    public void JiraConnectorConfig_ParsedFromAPreMcpRow_DefaultsToRestOnlyWithNoPolling()
    {
        // Existing installs stored only the three original fields; they must keep working untouched.
        const string legacyJson = """{"siteUrl":"https://acme.atlassian.net","email":"you@acme.com","projectKey":"SBRO"}""";

        var config = JsonSerializer.Deserialize<JiraConnectorConfig>(legacyJson, WebJson)!;

        Assert.Null(config.McpServerUrl);
        Assert.Equal(0, config.TriggerPollSeconds);
        Assert.Equal("SBRO", config.ProjectKey);
    }
}
