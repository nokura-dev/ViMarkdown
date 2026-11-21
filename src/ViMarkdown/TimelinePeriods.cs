namespace ViMarkdown;

public static class TimelinePeriods
{
    public static bool Matches(DateTimeOffset updated, int period, DateTime today)
    {
        today = today.Date;
        var monday = today.AddDays(-((int)today.DayOfWeek + 6) % 7);
        var month = new DateTime(today.Year, today.Month, 1);
        var (start, end) = period switch
        {
            1 => (today, today.AddDays(1)),
            2 => (monday, today.AddDays(1)),
            3 => (monday.AddDays(-7), monday),
            4 => (month, today.AddDays(1)),
            5 => (month.AddMonths(-1), month),
            6 => (month.AddMonths(-2), month.AddMonths(-1)),
            _ => (DateTime.MinValue, DateTime.MaxValue)
        };
        var date = updated.LocalDateTime;
        return period == 0 || date >= start && date < end;
    }
}
