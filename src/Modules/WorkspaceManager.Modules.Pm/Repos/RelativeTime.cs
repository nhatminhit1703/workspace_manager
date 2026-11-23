using System.Globalization;

namespace WorkspaceManager.Modules.Pm.Repos;

public static class RelativeTime
{
    /// <summary>"just now", "5 min ago", "3 h ago", "yesterday", "12 days ago", "4 months ago", "2 years ago".</summary>
    public static string Format(DateTimeOffset? time, DateTimeOffset now)
    {
        if (time is null)
        {
            return string.Empty;
        }

        var span = now - time.Value;
        if (span < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (span < TimeSpan.FromHours(1))
        {
            return Plural((int)span.TotalMinutes, "min");
        }

        if (span < TimeSpan.FromDays(1))
        {
            return Plural((int)span.TotalHours, "h");
        }

        if (span < TimeSpan.FromDays(2))
        {
            return "yesterday";
        }

        if (span < TimeSpan.FromDays(60))
        {
            return Plural((int)span.TotalDays, "day");
        }

        if (span < TimeSpan.FromDays(730))
        {
            return Plural((int)(span.TotalDays / 30.4), "month");
        }

        return Plural((int)(span.TotalDays / 365.25), "year");
    }

    /// <summary>"1 day ago", "2 days ago"; units "min" and "h" are abbreviations and never take an "s".</summary>
    private static string Plural(int n, string unit)
    {
        var s = n == 1 || unit is "min" or "h" ? unit : unit + "s";
        return string.Create(CultureInfo.InvariantCulture, $"{n} {s} ago");
    }
}
