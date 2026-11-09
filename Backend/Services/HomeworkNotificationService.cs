using System.Text;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBotApi.Data;
using TelegramBotApi.Models;

namespace TelegramBotApi.Services;

/// <summary>
/// Dispatches homework notifications to students and schedules their automatic self-destruction after 24 hours.
/// </summary>
public class HomeworkNotificationService
{
    private readonly ITelegramBotClient _botClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WhitelistService _whitelist;
    private readonly IConfiguration _config;
    private readonly ILogger<HomeworkNotificationService> _logger;

    public static readonly TimeSpan SelfDestructDelay = TimeSpan.FromHours(24);

    public HomeworkNotificationService(
        ITelegramBotClient botClient,
        IServiceScopeFactory scopeFactory,
        WhitelistService whitelist,
        IConfiguration config,
        ILogger<HomeworkNotificationService> logger)
    {
        _botClient = botClient;
        _scopeFactory = scopeFactory;
        _whitelist = whitelist;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Sends a Telegram message to all whitelisted students about newly published homework.
    /// Stores the sent message ID to delete it after 24 hours.
    /// </summary>
    public async Task NotifyStudentsAsync(
        int homeworkMessageId,
        string subjectName,
        string taskText,
        DateTime? deadline,
        long authorTelegramId,
        CancellationToken cancellationToken = default)
    {
        var targetUsers = _whitelist.GetAll();
        if (targetUsers.Count == 0) return;

        var webAppUrl = _config["Telegram:WebAppUrl"];
        InlineKeyboardMarkup? replyMarkup = null;
        if (IsValidTelegramWebAppUrl(webAppUrl))
        {
            replyMarkup = new InlineKeyboardMarkup(new[]
            {
                InlineKeyboardButton.WithWebApp("📱 Открыть задание в Mini App", new WebAppInfo { Url = webAppUrl! })
            });
        }

        var sb = new StringBuilder();
        sb.AppendLine("🔔 *Новое домашнее задание!*");
        sb.AppendLine($"📚 Предмет: *{EscapeMarkdown(subjectName)}*\n");

        if (deadline.HasValue)
        {
            var dl = deadline.Value;
            var dlFormatted = dl.ToString("dd.MM.yyyy HH:mm");
            var diff = dl - DateTime.UtcNow;
            string urgency = diff.TotalHours < 24 ? "⚠️ Меньше 24ч!" : $"осталось {(int)diff.TotalDays} дн.";
            sb.AppendLine($"⏳ Срок сдачи (дедлайн): `{dlFormatted}` ({urgency})\n");
        }

        sb.AppendLine("📝 *Задание:*");
        // Truncate long preview if necessary
        var previewText = taskText.Length > 800 ? taskText[..800] + "..." : taskText;
        sb.AppendLine(EscapeMarkdown(previewText));
        sb.AppendLine();
        sb.AppendLine("⏰ _Это сообщение автоматически удалится через 24 часа._");

        var text = sb.ToString();
        var deleteAt = DateTime.UtcNow.Add(SelfDestructDelay);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TelegramBotContext>();

        int sentCount = 0;

        foreach (var userId in targetUsers)
        {
            // Optional: skip notifying author if they posted it
            if (userId == authorTelegramId) continue;

            try
            {
                var sentMsg = await _botClient.SendMessage(
                    chatId: userId,
                    text: text,
                    replyMarkup: replyMarkup,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken
                );

                db.SentNotifications.Add(new SentNotification
                {
                    TelegramChatId = userId,
                    TelegramMessageId = sentMsg.Id,
                    HomeworkMessageId = homeworkMessageId,
                    SentAt = DateTime.UtcNow,
                    DeleteAt = deleteAt,
                    IsDeleted = false
                });

                sentCount++;

                // Throttling to respect Telegram API rate limits (max ~30 msgs/sec broadcast)
                await Task.Delay(50, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Не удалось отправить уведомление пользователю {UserId} (возможно, бот не был запущен)", userId);
            }
        }

        if (sentCount > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Уведомление о ДЗ #{HomeworkId} отправлено {Count} студентам. Запланировано удаление на {DeleteAt}",
                homeworkMessageId, sentCount, deleteAt);
        }
    }

    /// <summary>
    /// Immediately deletes all sent notification messages from Telegram chats if the homework is deleted early by admin.
    /// </summary>
    public async Task DeleteNotificationsForHomeworkAsync(int homeworkMessageId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TelegramBotContext>();

        var notifications = await db.SentNotifications
            .Where(n => n.HomeworkMessageId == homeworkMessageId && !n.IsDeleted)
            .ToListAsync(cancellationToken);

        if (notifications.Count == 0) return;

        foreach (var n in notifications)
        {
            try
            {
                await _botClient.DeleteMessage(n.TelegramChatId, n.TelegramMessageId, cancellationToken);
            }
            catch { /* Already deleted or unreachable */ }

            n.IsDeleted = true;
        }

        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Удалено {Count} уведомлений для удаленного ДЗ #{HomeworkId}", notifications.Count, homeworkMessageId);
    }

    private static bool IsValidTelegramWebAppUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
        if (url.Contains("localhost", StringComparison.OrdinalIgnoreCase)) return false;
        if (url.Contains("127.0.0.1")) return false;
        return true;
    }

    private static string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text
            .Replace("_", "\\_")
            .Replace("*", "\\*")
            .Replace("[", "\\[")
            .Replace("]", "\\]")
            .Replace("`", "\\`");
    }
}
