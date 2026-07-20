// Unit tests for the Jira webhook parser (spec-021 T030): HMAC-SHA256 validation, payload parsing, and the
// project / issue-type / created-event scope filter.
using System.Security.Cryptography;
using System.Text;
using DBAIAzure.Core.Models.DorWorkflow.Config;
using DBAIAzure.Web.Integrations.Jira;
using Xunit;

namespace DBAIAzure.Tests.Dor;

public sealed class JiraWebhookParserTests
{
    private const string Body = """
        {"webhookEvent":"jira:issue_created",
         "issue":{"key":"SBRO-1","fields":{"project":{"key":"SBRO"},"issuetype":{"name":"Story"}}}}
        """;

    [Fact]
    public void Signature_Valid_WhenHmacMatches()
    {
        var header = "sha256=" + ComputeHmac(Body, "secret");
        Assert.True(JiraWebhookParser.IsSignatureValid(Body, header, "secret"));
    }

    [Fact]
    public void Signature_Invalid_WhenTampered()
    {
        Assert.False(JiraWebhookParser.IsSignatureValid(Body, "sha256=deadbeef", "secret"));
    }

    [Fact]
    public void Signature_Invalid_WhenSecretOrHeaderMissing()
    {
        Assert.False(JiraWebhookParser.IsSignatureValid(Body, "sha256=abc", null));
        Assert.False(JiraWebhookParser.IsSignatureValid(Body, null, "secret"));
    }

    [Fact]
    public void Parse_ExtractsIssueFields()
    {
        var info = JiraWebhookParser.Parse(Body);

        Assert.NotNull(info);
        Assert.Equal("SBRO-1", info!.IssueKey);
        Assert.Equal("SBRO", info.ProjectKey);
        Assert.Equal("Story", info.IssueType);
        Assert.Contains("issue_created", info.WebhookEvent);
    }

    [Fact]
    public void Parse_ReturnsNull_ForMalformedOrNonIssue()
    {
        Assert.Null(JiraWebhookParser.Parse("not json"));
        Assert.Null(JiraWebhookParser.Parse("""{"webhookEvent":"jira:issue_created"}"""));
    }

    [Fact]
    public void ShouldProcess_TrueWithinScope_FalseOutside()
    {
        var jira = new DorJiraConfig { ProjectKeys = new[] { "SBRO" }, IssueTypes = new[] { "Story", "Bug" } };
        var info = JiraWebhookParser.Parse(Body)!;

        Assert.True(JiraWebhookParser.ShouldProcess(info, jira));
        Assert.False(JiraWebhookParser.ShouldProcess(info with { ProjectKey = "OTHER" }, jira));
        Assert.False(JiraWebhookParser.ShouldProcess(info with { IssueType = "Epic" }, jira));
        Assert.False(JiraWebhookParser.ShouldProcess(info with { WebhookEvent = "jira:issue_updated" }, jira));
    }

    private static string ComputeHmac(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }
}
