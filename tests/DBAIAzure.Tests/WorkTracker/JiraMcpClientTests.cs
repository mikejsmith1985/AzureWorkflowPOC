// Unit tests for the MCP-first Jira transport: argument templating, response parsing, and the trigger JQL.
// All pure — no MCP server, no Jira, no I/O.
using DBAIAzure.Web.Integrations.Jira;
using Xunit;

namespace DBAIAzure.Tests.WorkTracker;

public sealed class JiraMcpClientTests
{
    // ── Argument templating ───────────────────────────────────────────────────

    [Fact]
    public void BuildReadArguments_WithoutTemplate_UsesDefaultShapeAndJoinsWatchFields()
    {
        var arguments = JiraMcpClient.BuildReadArguments(null, "SBRO-12", new[] { "summary", "description" });

        Assert.Contains("\"issueIdOrKey\":\"SBRO-12\"", arguments);
        Assert.Contains("\"fields\":\"summary,description\"", arguments);
    }

    [Fact]
    public void BuildReadArguments_WithOverride_KeepsTheServersOwnArguments()
    {
        // A server needing an extra argument (here a cloud id) is expressible without a code change.
        var template = """{"cloudId":"abc-123","issueIdOrKey":"{{issueKey}}"}""";

        var arguments = JiraMcpClient.BuildReadArguments(template, "SBRO-9", Array.Empty<string>());

        Assert.Contains("\"cloudId\":\"abc-123\"", arguments);
        Assert.Contains("\"issueIdOrKey\":\"SBRO-9\"", arguments);
    }

    [Fact]
    public void BuildTransitionArguments_SubstitutesKeyAndTransitionId()
    {
        var arguments = JiraMcpClient.BuildTransitionArguments(null, "SBRO-3", "31");

        Assert.Contains("\"issueIdOrKey\":\"SBRO-3\"", arguments);
        Assert.Contains("\"id\":\"31\"", arguments);
    }

    [Fact]
    public void BuildSearchArguments_EmitsMaxResultsAsANumberNotAString()
    {
        var arguments = JiraMcpClient.BuildSearchArguments(null, "project = \"SBRO\"", 25);

        Assert.Contains("\"maxResults\":25", arguments);

        // The JQL carries quotes; whatever escape form is used, the arguments must stay valid JSON that hands
        // the tool back exactly the query we built.
        using var parsed = System.Text.Json.JsonDocument.Parse(arguments);
        Assert.Equal("project = \"SBRO\"", parsed.RootElement.GetProperty("jql").GetString());
        Assert.Equal(25, parsed.RootElement.GetProperty("maxResults").GetInt32());
    }

    // ── Read-issue parsing ────────────────────────────────────────────────────

    [Fact]
    public void ParseIssue_FlattensFieldsAndBuildsTheBrowseUrl()
    {
        const string content = """
            {"key":"SBRO-7","fields":{"summary":"Add login","status":{"name":"To Do"}}}
            """;

        var issue = JiraMcpClient.ParseIssue(content, "SBRO-7", "https://acme.atlassian.net", Array.Empty<string>());

        Assert.NotNull(issue);
        Assert.Equal("SBRO-7", issue!.Key);
        Assert.Equal("https://acme.atlassian.net/browse/SBRO-7", issue.Url);
        Assert.Equal("Add login", issue.Fields["summary"]);
        Assert.Equal("To Do", issue.Fields["status"]);   // option object flattened to its display label
    }

    [Fact]
    public void ParseIssue_WhenWrappedInAnIssueProperty_StillFindsTheIssue()
    {
        // Some MCP servers wrap the payload; both shapes must work.
        const string content = """{"issue":{"key":"SBRO-8","fields":{"summary":"Wrapped"}}}""";

        var issue = JiraMcpClient.ParseIssue(content, "SBRO-8", "https://acme.atlassian.net", Array.Empty<string>());

        Assert.NotNull(issue);
        Assert.Equal("Wrapped", issue!.Fields["summary"]);
    }

    [Fact]
    public void ParseIssue_ProjectsOnlyTheWatchedFields()
    {
        const string content = """
            {"key":"SBRO-4","fields":{"summary":"Keep","description":"Keep too","labels":["drop"]}}
            """;

        var issue = JiraMcpClient.ParseIssue(
            content, "SBRO-4", "https://acme.atlassian.net", new[] { "summary", "description" });

        Assert.NotNull(issue);
        Assert.Equal(new[] { "summary", "description" }, issue!.Fields.Keys.OrderByDescending(k => k).ToArray());
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"message":"tool failed"}""")]   // well-formed, but nothing issue-shaped
    public void ParseIssue_WhenNotIssueShaped_ReturnsNullSoTheCallerFallsBackToRest(string content)
    {
        Assert.Null(JiraMcpClient.ParseIssue(content, "SBRO-1", "https://acme.atlassian.net", Array.Empty<string>()));
    }

    [Fact]
    public void ParseIssue_RendersRichTextDescriptionsAsPlainText()
    {
        // Jira Cloud returns descriptions in Atlassian Document Format; the AI must see readable text.
        const string content = """
            {"key":"SBRO-2","fields":{"description":{"type":"doc","version":1,"content":[
              {"type":"paragraph","content":[{"type":"text","text":"As a user I want to log in"}]}]}}}
            """;

        var issue = JiraMcpClient.ParseIssue(content, "SBRO-2", "", Array.Empty<string>());

        Assert.Equal("As a user I want to log in", issue!.Fields["description"]);
    }

    // ── Search parsing ────────────────────────────────────────────────────────

    [Fact]
    public void ParseSearchResults_ReadsKeyProjectTypeAndCreatedAt()
    {
        const string content = """
            {"issues":[{"key":"SBRO-11","fields":{
                "project":{"key":"SBRO"},"issuetype":{"name":"Story"},"created":"2026-07-23T09:15:00.000+0000"}}]}
            """;

        var issue = Assert.Single(JiraMcpClient.ParseSearchResults(content));

        Assert.Equal("SBRO-11", issue.IssueKey);
        Assert.Equal("SBRO", issue.ProjectKey);
        Assert.Equal("Story", issue.IssueType);
        Assert.Equal(new DateTimeOffset(2026, 7, 23, 9, 15, 0, TimeSpan.Zero), issue.CreatedAt);
    }

    [Fact]
    public void ParseSearchResults_WhenProjectIsAbsent_DerivesItFromTheIssueKey()
    {
        var issue = Assert.Single(JiraMcpClient.ParseSearchResults("""[{"key":"ABC-42"}]"""));

        Assert.Equal("ABC", issue.ProjectKey);
        Assert.Null(issue.CreatedAt);
    }

    [Fact]
    public void ParseSearchResults_SkipsEntriesWithoutAKey()
    {
        var issues = JiraMcpClient.ParseSearchResults("""{"issues":[{"id":"1"},{"key":"SBRO-5"}]}""");

        Assert.Equal("SBRO-5", Assert.Single(issues).IssueKey);
    }

    [Fact]
    public void ParseSearchResults_WhenUnparseable_ReturnsEmptyRatherThanThrowing()
    {
        Assert.Empty(JiraMcpClient.ParseSearchResults("<html>gateway error</html>"));
    }

    // ── Trigger JQL ───────────────────────────────────────────────────────────

    [Fact]
    public void BuildCreatedSinceJql_OnAColdStart_LooksBackOverAShortWindowOnly()
    {
        var jql = JiraMcpClient.BuildCreatedSinceJql(
            new[] { "SBRO" }, new[] { "Story" }, createdAfter: null, lookbackMinutes: 15);

        Assert.Equal("""project in ("SBRO") AND issuetype in ("Story") AND created >= -15m ORDER BY created ASC""", jql);
    }

    [Fact]
    public void BuildCreatedSinceJql_AfterTheFirstSweep_AsksOnlyForNewerTickets()
    {
        var since = new DateTimeOffset(2026, 7, 23, 8, 30, 0, TimeSpan.Zero);

        var jql = JiraMcpClient.BuildCreatedSinceJql(new[] { "SBRO" }, Array.Empty<string>(), since, 15);

        Assert.Contains("""created > "2026/07/23 08:30" """.TrimEnd(), jql);
        Assert.DoesNotContain("issuetype", jql);   // no issue-type filter configured → no clause
    }

    [Fact]
    public void BuildCreatedSinceJql_WithNoScopeConfigured_StillBoundsTheQueryByTime()
    {
        var jql = JiraMcpClient.BuildCreatedSinceJql(
            Array.Empty<string>(), Array.Empty<string>(), createdAfter: null, lookbackMinutes: 15);

        Assert.Equal("created >= -15m ORDER BY created ASC", jql);
    }

    [Fact]
    public void BuildCreatedSinceJql_EscapesQuotesInAProjectKey()
    {
        // A crafted project key must not be able to terminate the literal and extend the query.
        var jql = JiraMcpClient.BuildCreatedSinceJql(
            new[] { "AB\"C" }, Array.Empty<string>(), createdAfter: null, lookbackMinutes: 15);

        Assert.Contains("""project in ("AB\"C")""", jql);
    }
}
