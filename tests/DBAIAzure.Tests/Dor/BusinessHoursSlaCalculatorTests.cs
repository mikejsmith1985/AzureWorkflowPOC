// Unit tests for the SLA deadline calculator (spec-021 T049): wall-clock, within-a-day business hours, and
// spanning a weekend. Uses UTC as the business-hours timezone so expectations are offset-free and deterministic.
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Core.Models.DorWorkflow.Config;
using Xunit;

namespace DBAIAzure.Tests.Dor;

public sealed class BusinessHoursSlaCalculatorTests
{
    private static DorSlaConfig BusinessHours(double primary = 24) => new()
    {
        PrimarySlaHours = primary,
        ClockType = "business_hours",
        BusinessHours = new DorBusinessHoursConfig
        {
            Timezone = "UTC", Start = "08:00", End = "17:00", WorkingDays = new[] { 1, 2, 3, 4, 5 },
        },
    };

    [Fact]
    public void WallClock_AddsElapsedHours()
    {
        var start = new DateTimeOffset(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);
        var sla = new DorSlaConfig { ClockType = "wall_clock" };

        var deadline = BusinessHoursSlaCalculator.ComputeDeadline(start, 5, sla);

        Assert.Equal(new DateTimeOffset(2026, 7, 17, 15, 0, 0, TimeSpan.Zero), deadline);
    }

    [Fact]
    public void BusinessHours_WithinOneDay()
    {
        // Wednesday 10:00 + 3 business hours = Wednesday 13:00 (inside the 08:00–17:00 window).
        var start = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);

        var deadline = BusinessHoursSlaCalculator.ComputeDeadline(start, 3, BusinessHours());

        Assert.Equal(new DateTimeOffset(2026, 7, 15, 13, 0, 0, TimeSpan.Zero), deadline);
    }

    [Fact]
    public void BusinessHours_SpansWeekend()
    {
        // Friday 16:00 + 2 business hours: 1h to Friday 17:00, then 1h into Monday → Monday 09:00.
        var start = new DateTimeOffset(2026, 7, 17, 16, 0, 0, TimeSpan.Zero); // Friday

        var deadline = BusinessHoursSlaCalculator.ComputeDeadline(start, 2, BusinessHours());

        Assert.Equal(new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero), deadline); // Monday
    }

    [Fact]
    public void BusinessHours_StartBeforeOpen_CountsFromOpen()
    {
        // Monday 06:00 + 1 business hour → counts from 08:00 open → Monday 09:00.
        var start = new DateTimeOffset(2026, 7, 20, 6, 0, 0, TimeSpan.Zero); // Monday, before open

        var deadline = BusinessHoursSlaCalculator.ComputeDeadline(start, 1, BusinessHours());

        Assert.Equal(new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero), deadline);
    }
}
