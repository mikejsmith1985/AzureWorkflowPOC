// Unit tests for SqliteAppHeartbeatStore: heartbeat record/read + raised-issue dedup (feature 013, US3).
using DBAIAzure.Core.Models;
using DBAIAzure.Storage.Repositories;
using Xunit;

namespace DBAIAzure.Tests.Apps;

/// <summary>Verifies heartbeat recording/reads and idempotent raised-issue dedup.</summary>
public sealed class AppHeartbeatStoreTests
{
    [Fact]
    public async Task RecordCycle_ThenGet_ReflectsLatest()
    {
        var (factory, connection) = AppTestDb.Create();
        await using (connection)
        {
            var sut = new SqliteAppHeartbeatStore(factory);

            await sut.RecordCycleAsync("app1", ok: true, error: null);
            await sut.RecordCycleAsync("app1", ok: false, error: "boom");

            var hb = await sut.GetAsync("app1");
            Assert.NotNull(hb);
            Assert.False(hb!.LastCycleOk);
            Assert.Equal("boom", hb.LastError);
            Assert.Equal(2, hb.CycleCount);
        }
    }

    [Fact]
    public async Task IsRaised_FalseUntilRecorded_ThenTrue_AndIdempotent()
    {
        var (factory, connection) = AppTestDb.Create();
        await using (connection)
        {
            var sut = new SqliteAppHeartbeatStore(factory);
            const string sig = "SIGabc";

            Assert.False(await sut.IsRaisedAsync(sig));

            await sut.RecordRaisedAsync(new AppRaisedIssue(sig, "app1", "run-1", DateTimeOffset.UtcNow));
            Assert.True(await sut.IsRaisedAsync(sig));

            // Idempotent — recording the same signature again must not throw or duplicate.
            await sut.RecordRaisedAsync(new AppRaisedIssue(sig, "app1", "run-2", DateTimeOffset.UtcNow));
            Assert.True(await sut.IsRaisedAsync(sig));
        }
    }

    [Fact]
    public async Task Get_UnknownApp_ReturnsNull()
    {
        var (factory, connection) = AppTestDb.Create();
        await using (connection)
        {
            var sut = new SqliteAppHeartbeatStore(factory);
            Assert.Null(await sut.GetAsync("nope"));
        }
    }
}
