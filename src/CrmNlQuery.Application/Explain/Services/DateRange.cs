using System.Globalization;

namespace CrmNlQuery.Application.Explain.Services;

/// <summary>
/// Converts relative date operators into concrete date ranges based on the server's UTC date, with weeks starting on Sunday.
/// </summary>
public static class DateRange
{
    /// <summary>
    /// Resolves a relative date operator and value to a from and to date for a given today.
    /// </summary>
    public static (DateOnly? From, DateOnly? To) Resolve(string op, string? value, DateOnly today)
    {
        try
        {
            return ResolveCore(op, value, today);
        }
        catch (ArgumentOutOfRangeException)
        {
            return (null, null);
        }
    }

    private static (DateOnly? From, DateOnly? To) ResolveCore(string op, string? value, DateOnly today)
    {
        switch (op)
        {
            case "today": return (today, today);
            case "yesterday": return (today.AddDays(-1), today.AddDays(-1));
            case "tomorrow": return (today.AddDays(1), today.AddDays(1));
            case "last-seven-days": return (today.AddDays(-7), today);
            case "this-week": return Week(today, 0);
            case "last-week": return Week(today, -1);
            case "next-week": return Week(today, 1);
            case "this-month": return Month(today, 0);
            case "last-month": return Month(today, -1);
            case "next-month": return Month(today, 1);
            case "this-year": return Year(today, 0);
            case "last-year": return Year(today, -1);
            case "next-year": return Year(today, 1);
            case "on":
                return Date(value) is DateOnly exact ? (exact, exact) : (null, null);
            case "on-or-after":
                return (Date(value), null);
            case "on-or-before":
                return (null, Date(value));
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 0 || n > 10000)
        {
            return (null, null);
        }
        switch (op)
        {
            case "last-x-days": return (today.AddDays(-n), today);
            case "next-x-days": return (today, today.AddDays(n));
            case "last-x-weeks": return (today.AddDays(-7 * n), today);
            case "next-x-weeks": return (today, today.AddDays(7 * n));
            case "last-x-months": return (today.AddMonths(-n), today);
            case "next-x-months": return (today, today.AddMonths(n));
            case "last-x-years": return (today.AddYears(-n), today);
            case "next-x-years": return (today, today.AddYears(n));
            case "olderthan-x-days": return (null, today.AddDays(-n));
            case "olderthan-x-weeks": return (null, today.AddDays(-7 * n));
            case "olderthan-x-months": return (null, today.AddMonths(-n));
            case "olderthan-x-years": return (null, today.AddYears(-n));
        }
        return (null, null);
    }

    private static (DateOnly?, DateOnly?) Week(DateOnly today, int offset)
    {
        var start = today.AddDays(-(int)today.DayOfWeek + 7 * offset);
        return (start, start.AddDays(6));
    }

    private static (DateOnly?, DateOnly?) Month(DateOnly today, int offset)
    {
        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(offset);
        return (first, first.AddMonths(1).AddDays(-1));
    }

    private static (DateOnly?, DateOnly?) Year(DateOnly today, int offset)
    {
        var first = new DateOnly(today.Year + offset, 1, 1);
        return (first, new DateOnly(first.Year, 12, 31));
    }

    private static DateOnly? Date(string? value)
    {
        if (value is null || value.Length < 10)
        {
            return null;
        }
        return DateOnly.TryParseExact(value.Substring(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }
}
