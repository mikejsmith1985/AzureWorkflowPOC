// Computes an SLA deadline from a clock start (spec-021 FR-016/FR-018). Supports wall-clock (a plain elapsed
// duration) and business-hours (only configured working days/hours in the configured timezone count). Pure and
// side-effect-free so it is trivially unit-testable.
using DBAIAzure.Core.Models.DorWorkflow.Config;

namespace DBAIAzure.Core.Models.DorWorkflow;

/// <summary>
/// Turns "N SLA hours from the clock start" into an absolute UTC deadline. Wall-clock simply adds the hours;
/// business-hours walks forward consuming only the configured working window (days + start/end) in the
/// configured timezone, so a breach can only occur during business hours.
/// </summary>
public static class BusinessHoursSlaCalculator
{
    /// <summary>Computes the UTC deadline for <paramref name="slaHours"/> measured from <paramref name="startUtc"/>.</summary>
    public static DateTimeOffset ComputeDeadline(DateTimeOffset startUtc, double slaHours, DorSlaConfig sla)
    {
        if (!string.Equals(sla.ClockType, "business_hours", StringComparison.OrdinalIgnoreCase))
            return startUtc.AddHours(slaHours);

        var timezone = ResolveTimezone(sla.BusinessHours.Timezone);
        var workingDays = new HashSet<int>(sla.BusinessHours.WorkingDays);
        var open = ParseTimeOfDay(sla.BusinessHours.Start);
        var close = ParseTimeOfDay(sla.BusinessHours.End);
        if (close <= open || workingDays.Count == 0)
            return startUtc.AddHours(slaHours); // misconfigured window → fall back to wall-clock

        var remaining = TimeSpan.FromHours(slaHours);
        var cursor = TimeZoneInfo.ConvertTime(startUtc, timezone).DateTime; // local wall time in the tz

        // Guard against pathological configs (e.g. a huge SLA) — cap the walk at a generous number of days.
        for (var guard = 0; remaining > TimeSpan.Zero && guard < 3660; guard++)
        {
            var todayOpen = cursor.Date + open;
            var todayClose = cursor.Date + close;

            if (!workingDays.Contains(IsoDayOfWeek(cursor)) || cursor >= todayClose)
            {
                cursor = cursor.Date.AddDays(1) + open; // jump to the next day's open; the loop re-checks the day
                continue;
            }

            if (cursor < todayOpen)
                cursor = todayOpen;

            var availableToday = todayClose - cursor;
            if (availableToday >= remaining)
            {
                cursor = cursor.Add(remaining);
                remaining = TimeSpan.Zero;
            }
            else
            {
                remaining -= availableToday;
                cursor = cursor.Date.AddDays(1) + open;
            }
        }

        var offset = timezone.GetUtcOffset(cursor);
        return new DateTimeOffset(cursor, offset).ToUniversalTime();
    }

    /// <summary>ISO day of week (Mon=1 … Sun=7) for the given local time.</summary>
    private static int IsoDayOfWeek(DateTime moment) =>
        moment.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)moment.DayOfWeek;

    private static TimeSpan ParseTimeOfDay(string value) =>
        TimeSpan.TryParse(value, out var parsed) ? parsed : new TimeSpan(9, 0, 0);

    private static TimeZoneInfo ResolveTimezone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return TimeZoneInfo.Utc; }
    }
}
