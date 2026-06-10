using DBAIAzure.Core.Models;
using DBAIAzure.Processes;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Unit tests for validation logic that does not require a live LLM or SK process runtime.
/// These cover the pure-function pieces: JSON parsing and the Fibonacci clamping guard.
/// </summary>
public class ValidationStepTests
{
    // ── DorVerdict deserialization ─────────────────────────────────────────────

    [Fact]
    public void DorVerdict_Deserializes_ReadyResponse()
    {
        var json = """{"is_ready":true,"missing_fields":[],"reasoning":"All fields present."}""";

        var verdict = System.Text.Json.JsonSerializer.Deserialize<DorVerdict>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(verdict);
        Assert.True(verdict.IsReady);
        Assert.Empty(verdict.MissingFields);
    }

    [Fact]
    public void DorVerdict_Deserializes_NotReadyResponse()
    {
        var json = """{"is_ready":false,"missing_fields":["acceptance criteria"],"reasoning":"Missing AC."}""";

        var verdict = System.Text.Json.JsonSerializer.Deserialize<DorVerdict>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(verdict);
        Assert.False(verdict.IsReady);
        Assert.Single(verdict.MissingFields);
    }

    // ── Fibonacci clamping (from EstimationStep logic) ─────────────────────────

    [Theory]
    [InlineData(0, 1)]    // below range → 1
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 3)]    // midpoint between 3 and 5 → 3 (closer)
    [InlineData(6, 5)]    // closer to 5 than to 8
    [InlineData(9, 8)]    // closer to 8 than to 13
    [InlineData(21, 21)]
    [InlineData(100, 21)] // above range → 21
    public void FibonacciClamp_ReturnsNearestValidPoint(int input, int expected)
    {
        var validPoints = new[] { 1, 2, 3, 5, 8, 13, 21 };
        var actual = validPoints.MinBy(p => Math.Abs(p - input));
        Assert.Equal(expected, actual);
    }

    // ── ClarificationRound cap ─────────────────────────────────────────────────

    [Fact]
    public void TicketState_ClarificationRound_DefaultsToZero()
    {
        var ticket = new TicketState
        {
            TicketId = "INC0001",
            Title = "Some ticket",
            Description = "Some description.",
        };

        Assert.Equal(0, ticket.ClarificationRound);
    }

    [Fact]
    public void TicketState_WithClarification_IsImmutable()
    {
        var original = new TicketState
        {
            TicketId = "INC0001",
            Title = "Some ticket",
            Description = "Some description.",
        };

        var updated = original with { ClarificationRound = 1, HumanAnswer = "The answer" };

        Assert.Equal(0, original.ClarificationRound);
        Assert.Null(original.HumanAnswer);
        Assert.Equal(1, updated.ClarificationRound);
        Assert.Equal("The answer", updated.HumanAnswer);
    }

    // ── Event name constants ───────────────────────────────────────────────────

    [Fact]
    public void Events_AllConstantsDefined()
    {
        // Guard against accidental renaming that would break process wiring
        Assert.Equal("TicketReceived", Events.TicketReceived);
        Assert.Equal("ReadyPath", Events.ReadyPath);
        Assert.Equal("NotReadyPath", Events.NotReadyPath);
        Assert.Equal("Blocked", Events.Blocked);
        Assert.Equal("HumanResponded", Events.HumanResponded);
        Assert.Equal("EstimationComplete", Events.EstimationComplete);
    }
}
