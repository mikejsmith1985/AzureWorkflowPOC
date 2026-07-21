// Unit tests for the DoR config card's split/merge form (spec-021 US6). Prove the card round-trips the DoR
// document and dry-run fields through the six-namespace blob, emits snake_case the resolver reads back, and that
// its starter template is a valid, health-check-passing baseline. Pure — no infrastructure (Article V, <10ms).
using System.Text.Json;
using DBAIAzure.Core.Models.DorWorkflow.Config;
using DBAIAzure.Web.Services.Dor;
using Xunit;

namespace DBAIAzure.Tests.Dor;

public sealed class DorConfigCardFormTests
{
    // The resolver reads config back with a snake_case, case-insensitive policy — the card must emit the same
    // shape, so these tests deserialize the card's output exactly as the running workflow would.
    private static readonly JsonSerializerOptions ResolverOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void Parse_Null_ReturnsInlineDefaults()
    {
        var form = DorConfigCardForm.Parse(null);

        Assert.Equal("inline", form.SourceType);
        Assert.Equal(string.Empty, form.InlineMarkdown);
        Assert.False(form.DryRun);
        Assert.Equal(string.Empty, form.OtherConfigJson);
    }

    [Fact]
    public void Parse_ExtractsDorAndRun_AndStripsThemFromOtherJson()
    {
        const string full = """
            {
              "jira": { "project_keys": ["SBRO"], "ready_transition_id": "31" },
              "dor": { "source_type": "inline", "inline_markdown": "# DoR\n1. Summary", "cache_ttl_minutes": 30 },
              "run": { "dry_run": true }
            }
            """;

        var form = DorConfigCardForm.Parse(full);

        Assert.Equal("inline", form.SourceType);
        Assert.Equal("# DoR\n1. Summary", form.InlineMarkdown);
        Assert.Equal(30, form.CacheTtlMinutes);
        Assert.True(form.DryRun);
        // The DoR document and dry-run are now first-class fields, not left in the JSON block.
        Assert.DoesNotContain("\"dor\"", form.OtherConfigJson);
        Assert.DoesNotContain("\"run\"", form.OtherConfigJson);
        Assert.Contains("project_keys", form.OtherConfigJson);
    }

    [Fact]
    public void ToConfigJson_Inline_RoundTripsThroughResolverOptions()
    {
        var form = new DorConfigCardForm
        {
            SourceType = "inline",
            InlineMarkdown = "# Definition of Ready\n1. Summary present",
            DryRun = true,
            OtherConfigJson = """{ "jira": { "project_keys": ["SBRO"], "ready_transition_id": "31" } }""",
        };

        var config = JsonSerializer.Deserialize<DorWorkflowConfig>(form.ToConfigJson(), ResolverOptions)!;

        Assert.Equal("inline", config.Dor.SourceType);
        Assert.Equal("# Definition of Ready\n1. Summary present", config.Dor.InlineMarkdown);
        Assert.True(config.Run.DryRun);
        // The untouched namespace survives the merge.
        Assert.Equal(new[] { "SBRO" }, config.Jira.ProjectKeys);
        Assert.Equal("31", config.Jira.ReadyTransitionId);
    }

    [Fact]
    public void ToConfigJson_Url_SetsSourceUriAndTtl_AndOmitsInline()
    {
        var form = new DorConfigCardForm
        {
            SourceType = "url",
            SourceUri = "https://wiki/dor",
            CacheTtlMinutes = 5,
            OtherConfigJson = "{}",
        };

        var config = JsonSerializer.Deserialize<DorWorkflowConfig>(form.ToConfigJson(), ResolverOptions)!;

        Assert.Equal("url", config.Dor.SourceType);
        Assert.Equal("https://wiki/dor", config.Dor.SourceUri);
        Assert.Equal(5, config.Dor.CacheTtlMinutes);
        Assert.True(string.IsNullOrEmpty(config.Dor.InlineMarkdown));
    }

    [Fact]
    public void ToConfigJson_InvalidJsonBlock_ThrowsFormatException()
    {
        var form = new DorConfigCardForm { OtherConfigJson = "{ not valid json" };

        Assert.Throws<FormatException>(() => form.ToConfigJson());
    }

    [Fact]
    public void ToConfigJson_NonObjectJsonBlock_ThrowsFormatException()
    {
        var form = new DorConfigCardForm { OtherConfigJson = "[1, 2, 3]" };

        Assert.Throws<FormatException>(() => form.ToConfigJson());
    }

    [Fact]
    public void CreateStarter_ProducesConfig_ThatPassesValidation()
    {
        var config = JsonSerializer.Deserialize<DorWorkflowConfig>(
            DorConfigCardForm.CreateStarter().ToConfigJson(), ResolverOptions)! with { IsConfigured = true };

        // A brand-new operator gets a working baseline: no validation issues, and dry-run on for safety.
        Assert.Empty(DorConfigValidation.Validate(config));
        Assert.True(config.Run.DryRun);
        Assert.Equal("inline", config.Dor.SourceType);
        Assert.False(string.IsNullOrWhiteSpace(config.Dor.InlineMarkdown));
    }

    [Fact]
    public void BuildSecretsJson_OnlyIncludesEnteredSecrets_AsSnakeCase()
    {
        var json = DorConfigCardForm.BuildSecretsJson(
            jiraApiToken: "tok", jiraWebhookSecret: null, slackToken: "  ", aiApiKey: "key");

        Assert.NotNull(json);
        var secrets = JsonSerializer.Deserialize<DorWorkflowSecrets>(json!, ResolverOptions)!;
        Assert.Equal("tok", secrets.JiraApiToken);
        Assert.Equal("key", secrets.AiApiKey);
        // Blank / whitespace fields are omitted so an existing stored secret is preserved (Article IX).
        Assert.Null(secrets.JiraWebhookSecret);
        Assert.Null(secrets.SlackToken);
    }

    [Fact]
    public void BuildSecretsJson_AllBlank_ReturnsNull()
    {
        Assert.Null(DorConfigCardForm.BuildSecretsJson(null, "", "   ", null));
    }
}
