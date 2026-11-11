using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramBotApi.Services.Bot;

/// <summary>
/// Mini App URL from configuration (Telegram:WebAppUrl) and buttons that open it.
/// Telegram only opens Mini Apps from public HTTPS addresses.
/// </summary>
public static class MiniAppLink
{
    public static string? GetUrl(IConfiguration config)
    {
        var url = config["Telegram:WebAppUrl"];
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;
        if (url.Contains("localhost", StringComparison.OrdinalIgnoreCase) || url.Contains("127.0.0.1")) return null;
        return url;
    }

    /// <summary>Inline keyboard with a single "open Mini App" button, or null if no public URL is configured.</summary>
    public static InlineKeyboardMarkup? Button(IConfiguration config, string text)
    {
        var url = GetUrl(config);
        return url == null
            ? null
            : new InlineKeyboardMarkup(InlineKeyboardButton.WithWebApp(text, new WebAppInfo { Url = url }));
    }
}
