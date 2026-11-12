using TelegramBotApi.Models;

namespace TelegramBotApi.Services;

/// <summary>
/// Decides whether and how a user gets a notification, based on their <see cref="NotificationSettings"/>.
/// Pure functions — easy to test. A null settings object means "defaults".
/// </summary>
public static class NotificationPolicy
{
    public static bool ShouldNotify(NotificationSettings? settings, IReadOnlySet<int> mutedSubjects, int subjectId) =>
        (settings?.NewTasks ?? true) && !mutedSubjects.Contains(subjectId);

    public static bool ShouldAutoDelete(NotificationSettings? settings) => settings?.AutoDelete ?? true;

    /// <summary>
    /// True when the local time falls into the quiet hours. Supports ranges over midnight (22:00–08:00).
    /// The start is inclusive, the end is exclusive. Equal start and end mean "no quiet hours".
    /// </summary>
    public static bool IsQuietTime(NotificationSettings? settings, TimeOnly localTime)
    {
        if (settings is not { QuietHoursEnabled: true }) return false;

        var from = settings.QuietFromMinutes;
        var to = settings.QuietToMinutes;
        if (from == to) return false;

        var now = localTime.Hour * 60 + localTime.Minute;
        return from < to
            ? now >= from && now < to          // e.g. 13:00–15:00
            : now >= from || now < to;         // over midnight, e.g. 22:00–08:00
    }

    public static string FormatMinutes(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

    /// <summary>Parses "HH:mm" into minutes after midnight.</summary>
    public static bool TryParseTime(string? value, out int minutes)
    {
        minutes = 0;
        if (!TimeOnly.TryParseExact(value, "HH:mm", out var time)) return false;
        minutes = time.Hour * 60 + time.Minute;
        return true;
    }
}
