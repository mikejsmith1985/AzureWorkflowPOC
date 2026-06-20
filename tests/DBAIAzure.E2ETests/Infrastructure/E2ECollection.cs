// Declares the xUnit collection that wires WebAppFixture and PlaywrightFixture together.
// All E2E test classes decorated with [Collection(E2ECollection.Name)] share one web app
// process and one Chromium browser instance, which keeps total test runtime reasonable.
namespace DBAIAzure.E2ETests.Infrastructure;

/// <summary>
/// xUnit collection definition — groups all E2E tests so they share the web app process
/// and browser instance rather than starting/stopping them for every test class.
/// </summary>
[CollectionDefinition(Name)]
public sealed class E2ECollection
    : ICollectionFixture<WebAppFixture>,
      ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "E2E";
}
