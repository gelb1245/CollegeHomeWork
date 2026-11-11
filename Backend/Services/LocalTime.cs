namespace TelegramBotApi.Services;

/// <summary>
/// Converts UTC times from the database to the college's local time zone for bot messages.
/// Mini App shows times in the user's device zone itself; the bot has no such information.
/// Configure with APP_TIME_ZONE / App:TimeZone (IANA id), default "Europe/Kyiv".
/// </summary>
public static class LocalTime
{
    private static TimeZoneInfo _zone = TimeZoneInfo.Utc;

    public static void Configure(IConfiguration config, ILogger? logger = null)
    {
        var id = Environment.GetEnvironmentVariable("APP_TIME_ZONE") ?? config["App:TimeZone"] ?? "Europe/Kyiv";
        try
        {
            _zone = TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger?.LogWarning("Часовой пояс {Zone} не найден, используется UTC", id);
            _zone = TimeZoneInfo.Utc;
        }
    }

    public static DateTime FromUtc(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zone);
}
