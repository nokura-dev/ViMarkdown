namespace ViMarkdown;

public static class TimelinePeriods
{
    public static string FormatUpdatedAt(DateTimeOffset updated, DateTime today)
    {
        if (updated == DateTimeOffset.MinValue) return "更新日時不明";
        var local = updated.LocalDateTime;
        var label = Matches(updated, 1, today) ? "今日"
            : Matches(updated, 2, today) ? "昨日"
            : Matches(updated, 3, today) ? local.DayOfWeek switch
            {
                DayOfWeek.Monday => "月曜",
                DayOfWeek.Tuesday => "火曜",
                DayOfWeek.Wednesday => "水曜",
                DayOfWeek.Thursday => "木曜",
                DayOfWeek.Friday => "金曜",
                DayOfWeek.Saturday => "土曜",
                _ => "日曜"
            }
            : null;
        return label is null ? local.ToString(PageNaming.TimestampFormat) : label + " " + local.ToString("HH:mm");
    }

    public static bool Matches(DateTimeOffset updated, int period, DateTime today)
    {
        today = today.Date;
        var monday = today.AddDays(-((int)today.DayOfWeek + 6) % 7);
        var month = new DateTime(today.Year, today.Month, 1);
        var (start, end) = period switch
        {
            1 => (today, today.AddDays(1)),
            2 => (today.AddDays(-1), today),
            3 => (monday, today.AddDays(1)),
            4 => (monday.AddDays(-7), monday),
            5 => (month, today.AddDays(1)),
            6 => (month.AddMonths(-1), month),
            7 => (month.AddMonths(-2), month.AddMonths(-1)),
            _ => (DateTime.MinValue, DateTime.MaxValue)
        };
        var date = updated.LocalDateTime;
        return period == 0 || date >= start && date < end;
    }
}
