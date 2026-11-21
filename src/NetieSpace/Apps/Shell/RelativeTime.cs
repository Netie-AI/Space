using System.Globalization;

namespace NetieSpace.Apps.Shell;

/// <summary>"When" as a person says it: "Just now", "12 min ago", "Today 14:05", "Yesterday 09:12", "3 days ago", "4 Mar".</summary>
public static class RelativeTime
{
    public static string Format(DateTime thenLocal, DateTime nowLocal)
    {
        var inv = CultureInfo.InvariantCulture;
        var age = nowLocal - thenLocal;
        if (age < TimeSpan.Zero)
            return thenLocal.ToString("d MMM HH:mm", inv);
        if (age < TimeSpan.FromMinutes(1))
            return "Just now";
        if (age < TimeSpan.FromMinutes(60))
            return $"{(int)age.TotalMinutes} min ago";
        var days = (nowLocal.Date - thenLocal.Date).Days;
        if (days == 0)
            return "Today " + thenLocal.ToString("HH:mm", inv);
        if (days == 1)
            return "Yesterday " + thenLocal.ToString("HH:mm", inv);
        if (days < 7)
            return $"{days} days ago";
        return thenLocal.Year == nowLocal.Year
            ? thenLocal.ToString("d MMM", inv)
            : thenLocal.ToString("d MMM yyyy", inv);
    }
}
