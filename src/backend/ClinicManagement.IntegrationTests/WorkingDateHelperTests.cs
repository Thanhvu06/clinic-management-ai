using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ClinicManagement.IntegrationTests;

public class WorkingDateHelperTests
{
    // Monday 12/10/2026 .. Sunday 18/10/2026
    public static IEnumerable<object[]> EveryWeekday() =>
        Enumerable.Range(0, 7).Select(i => new object[] { new DateOnly(2026, 10, 12).AddDays(i) });

    [Theory]
    [MemberData(nameof(EveryWeekday))]
    public void WorkingDateAfter_IsDistinct_NeverSunday_AndStrictlyIncreasing(DateOnly today)
    {
        var dates = Enumerable.Range(0, 61).Select(n => IntegrationTestBase.WorkingDateAfter(today, n)).ToList();

        Assert.Equal(dates.Count, dates.Distinct().Count());
        Assert.DoesNotContain(dates, d => d.DayOfWeek == DayOfWeek.Sunday);
        for (var n = 1; n < dates.Count; n++)
        {
            Assert.True(dates[n] > dates[n - 1], $"today={today:yyyy-MM-dd} n={n}: {dates[n]:yyyy-MM-dd} <= {dates[n - 1]:yyyy-MM-dd}");
        }
    }

    [Fact]
    public void WorkingDateAfter_FromThursday15Oct2026_SkipsSundayWithoutCollision()
    {
        var today = new DateOnly(2026, 10, 15);
        Assert.Equal(DayOfWeek.Thursday, today.DayOfWeek);

        Assert.Equal(new DateOnly(2026, 10, 19), IntegrationTestBase.WorkingDateAfter(today, 3));
        Assert.Equal(new DateOnly(2026, 10, 20), IntegrationTestBase.WorkingDateAfter(today, 4));
    }

    [Fact]
    public void WorkingDateAfter_NonPositive_KeepsTodayOrRollsSundayToMonday()
    {
        Assert.Equal(new DateOnly(2026, 10, 15), IntegrationTestBase.WorkingDateAfter(new DateOnly(2026, 10, 15), 0));
        Assert.Equal(new DateOnly(2026, 10, 19), IntegrationTestBase.WorkingDateAfter(new DateOnly(2026, 10, 18), 0));
        Assert.Equal(new DateOnly(2026, 10, 19), IntegrationTestBase.WorkingDateAfter(new DateOnly(2026, 10, 18), -2));
    }
}
