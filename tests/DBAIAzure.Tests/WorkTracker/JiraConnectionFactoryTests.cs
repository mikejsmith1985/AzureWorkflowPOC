// Unit tests for the per-run Jira connection factory (spec-020, T017).
using System.Net.Http.Headers;
using System.Text;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Web.Integrations.Jira;
using Xunit;

namespace DBAIAzure.Tests.WorkTracker;

/// <summary>
/// Verifies the Jira connection factory resolves credentials from the store per call, sets Basic auth +
/// BaseAddress, rebuilds the client only when credentials change, and refuses when Jira is not the active
/// provider — the behaviour that makes UI-entered Jira credentials work without a restart.
/// </summary>
public class JiraConnectionFactoryTests
{
    private const string JiraJson = """{"provider":"Jira","siteUrl":"https://x.atlassian.net","email":"a@b.c","projectKey":"PROJ"}""";
    private const string SecretJson = """{"apiToken":"tok-1"}""";

    [Fact]
    public async Task GetConnection_SetsBaseAddressAndBasicAuth_FromStore()
    {
        var factory = new JiraConnectionFactory(new StubResolver(JiraJson, SecretJson));

        var connection = await factory.GetConnectionAsync();

        Assert.Equal("https://x.atlassian.net/", connection.Client.BaseAddress!.ToString());
        Assert.Equal("PROJ", connection.ProjectKey);
        var expected = Convert.ToBase64String(Encoding.ASCII.GetBytes("a@b.c:tok-1"));
        Assert.Equal(new AuthenticationHeaderValue("Basic", expected).ToString(),
            connection.Client.DefaultRequestHeaders.Authorization!.ToString());
    }

    [Fact]
    public async Task GetConnection_ReusesClient_WhenCredentialsUnchanged()
    {
        var factory = new JiraConnectionFactory(new StubResolver(JiraJson, SecretJson));

        var first = await factory.GetConnectionAsync();
        var second = await factory.GetConnectionAsync();

        Assert.Same(first.Client, second.Client);
    }

    [Fact]
    public async Task GetConnection_RebuildsClient_WhenTokenRotated()
    {
        var resolver = new StubResolver(JiraJson, SecretJson);
        var factory = new JiraConnectionFactory(resolver);

        var first = await factory.GetConnectionAsync();
        resolver.Secret = """{"apiToken":"tok-2"}""";   // operator rotates the token in the UI
        var second = await factory.GetConnectionAsync();

        Assert.NotSame(first.Client, second.Client);
    }

    [Fact]
    public async Task GetConnection_Throws_WhenActiveProviderIsNotJira()
    {
        var adoJson = """{"provider":"AzureDevOps","organizationUrl":"https://dev.azure.com/o","projectName":"P"}""";
        var factory = new JiraConnectionFactory(new StubResolver(adoJson, secret: null));

        await Assert.ThrowsAsync<JiraNotConfiguredException>(() => factory.GetConnectionAsync());
    }

    [Fact]
    public async Task GetConnection_Throws_WhenTokenMissing()
    {
        var factory = new JiraConnectionFactory(new StubResolver(JiraJson, secret: null));

        await Assert.ThrowsAsync<JiraNotConfiguredException>(() => factory.GetConnectionAsync());
    }

    /// <summary>Stub resolver returning a preset (mutable) Jira config + secret.</summary>
    private sealed class StubResolver : IWorkTrackerConfigResolver
    {
        private readonly string _nonSecret;
        public string? Secret { get; set; }

        public StubResolver(string nonSecret, string? secret)
        {
            _nonSecret = nonSecret;
            Secret = secret;
        }

        public Task<ResolvedWorkTrackerConfig> ResolveActiveAsync(CancellationToken ct = default)
        {
            var provider = _nonSecret.Contains("\"Jira\"") ? WorkTrackerProvider.Jira : WorkTrackerProvider.AzureDevOps;
            return Task.FromResult(new ResolvedWorkTrackerConfig(provider, _nonSecret, Secret, IsConfigured: true));
        }
    }
}
