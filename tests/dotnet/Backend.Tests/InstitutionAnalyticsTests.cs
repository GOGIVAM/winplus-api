using System;
using Backend.Services;
using Xunit;

namespace Backend.Tests;

public class InstitutionAnalyticsTests
{
    [Theory]
    [InlineData("2026-11-27", "7d", "2026-11-21")]
    [InlineData("2026-11-27", "30d", "2026-10-29")]
    [InlineData("2026-11-27", "term", "2026-09-01")]
    [InlineData("2027-02-10", "term", "2027-01-01")]
    [InlineData("2027-05-10", "term", "2027-04-01")]
    [InlineData("2027-07-20", "term", "2027-04-22")]
    [InlineData("2026-11-27", "year", "2026-09-01")]
    [InlineData("2027-03-01", "year", "2026-09-01")]
    public void Periodes(string today, string period, string expectedFrom)
    {
        var now = DateTime.SpecifyKind(DateTime.Parse(today).AddHours(12), DateTimeKind.Utc);
        var p = InstitutionAnalytics.ResolvePeriod(period, now);
        Assert.NotNull(p);
        Assert.Equal(DateTime.Parse(expectedFrom).Date, p!.Value.From.Date);
        Assert.Equal(now, p.Value.To);
    }

    [Fact]
    public void Periode_inconnue() => Assert.Null(InstitutionAnalytics.ResolvePeriod("semaine", DateTime.UtcNow));
}
