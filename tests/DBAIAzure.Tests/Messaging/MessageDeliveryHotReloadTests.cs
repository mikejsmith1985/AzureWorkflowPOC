// Unit test for Messaging hot-reload — config changes are picked up on the next send without restart (T009a, FR-015).
using System.Net;
using System.Text.Json;
using DBAIAzure.Connectors.Messaging;
using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests.Messaging;

/// <summary>
/// Proves <see cref="MessageDelivery"/> resolves the connector's configuration and secrets at each call,
/// so an operator's change to the platform/webhook takes effect on the very next send with no restart.
/// </summary>
public sealed class MessageDeliveryHotReloadTests
{
    [Fact]
    public async Task ConfigChange_IsReflectedOnNextSend()
    {
        var repo = new StubConnectorConfigRepository
        {
            NonSecretConfigJson = JsonSerializer.Serialize(new { platform = nameof(MessagingPlatform.Slack) }),
            DecryptedSecretsJson = """{"webhookUrl":"https://hooks.example/slack"}""",
        };
        var handler = new CapturingHttpHandler(HttpStatusCode.OK, "ok");
        var delivery = new MessageDelivery(repo, new SingleHandlerHttpClientFactory(handler),
            [new TeamsWebhookProfile(), new SlackWebhookProfile(), new DiscordWebhookProfile()]);

        var first = await delivery.SendAsync("one");
        Assert.Equal(MessagingPlatform.Slack, first.Platform);
        Assert.Equal("https://hooks.example/slack", handler.LastRequestUri!.ToString());

        // Operator switches the connector to Teams with a different webhook — no restart.
        repo.NonSecretConfigJson = JsonSerializer.Serialize(new { platform = nameof(MessagingPlatform.Teams) });
        repo.DecryptedSecretsJson = """{"webhookUrl":"https://teams.example/hook"}""";

        var second = await delivery.SendAsync("two");
        Assert.Equal(MessagingPlatform.Teams, second.Platform);
        Assert.Equal("https://teams.example/hook", handler.LastRequestUri!.ToString());
        Assert.True(repo.GetAsyncCallCount >= 2, "config should be resolved on every send");
    }
}
