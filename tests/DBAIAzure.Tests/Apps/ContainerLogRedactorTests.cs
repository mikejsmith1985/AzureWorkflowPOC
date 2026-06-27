// Unit tests for ContainerLogRedactor secret stripping (feature 013, US2).
using DBAIAzure.Connectors.Apps;
using Xunit;

namespace DBAIAzure.Tests.Apps;

/// <summary>Verifies known secrets and common credential patterns are masked, and no secret survives.</summary>
public sealed class ContainerLogRedactorTests
{
    [Fact]
    public void Redact_KnownSecretValue_IsMasked()
    {
        const string token = "ghp_abcdEFGH1234567890";
        var redacted = ContainerLogRedactor.Redact($"cloning with token {token} ok", token);

        Assert.DoesNotContain(token, redacted);
        Assert.Contains("REDACTED", redacted);
    }

    [Fact]
    public void Redact_BearerHeader_IsMasked()
    {
        var redacted = ContainerLogRedactor.Redact("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload.sig");
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", redacted);
    }

    [Fact]
    public void Redact_KeyAssignment_IsMasked()
    {
        var redacted = ContainerLogRedactor.Redact("env: API_KEY=sk-secret-value password=hunter2");
        Assert.DoesNotContain("sk-secret-value", redacted);
        Assert.DoesNotContain("hunter2", redacted);
    }

    [Fact]
    public void Redact_UrlInlineCredentials_AreMasked()
    {
        var redacted = ContainerLogRedactor.Redact("git clone https://user:p@ssw0rd@github.com/x/y.git");
        Assert.DoesNotContain("p@ssw0rd", redacted);
    }

    [Fact]
    public void Redact_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ContainerLogRedactor.Redact(null));
        Assert.Equal(string.Empty, ContainerLogRedactor.Redact(""));
    }

    [Fact]
    public void Redact_PlainLogs_Unchanged()
    {
        const string logs = "npm ci\nadded 42 packages\nbuild complete\n";
        Assert.Equal(logs, ContainerLogRedactor.Redact(logs));
    }
}
