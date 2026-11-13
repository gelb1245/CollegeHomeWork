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

    /// <summary>"Delete never" for users who turned auto-delete off (still deleted together with the task).</summary>
    public static readonly DateTime KeepForever = new(9999, 1, 1, 0, 0, 0, DateTimeKind.Utc);

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
    /// Sends a Telegram message to all whitelisted students about newly published homework,
    /// respecting each user's notification settings (off, muted subject, quiet hours, auto-delete).
    /// Stores the sent message ID to delete it after 24 hours.
    /// </summary>
    public async Task NotifyStudentsAsync(
        int homeworkMessageId,
        int subjectId,
        string subjectName,
        string taskText,
        DateTime? deadline,
        long authorTelegramId,
        int attachmentCount = 0,
        TaskKind kind = TaskKind.Homework,
        CancellationToken cancellationToken = default)
    {
        var targetUsers = _whitelist.GetAll();
        if (targetUsers.Count == 0) return;

        var replyMarkup = Bot.MiniAppLink.Button(_config, "📱 Открыть задание в Mini App");

        var sb = new StringBuilder();
        sb.AppendLine(kind == TaskKind.Lecture ? "📖 *Новая лекция*" : "🔔 *Новое домашнее задание!*");
        sb.AppendLine($"📚 Предмет: *{EscapeMarkdown(subjectName)}*\n");

        if (deadline.HasValue)
        {
            var dl = deadline.Value;
            var dlFormatted = LocalTime.FromUtc(dl).ToString("dd.MM.yyyy HH:mm");
            var diff = dl - DateTime.UtcNow;
            string urgency = diff.TotalHours < 24 ? "⚠️ Меньше 24ч!" : $"осталось {(int)diff.TotalDays} дн.";
            sb.AppendLine($"⏳ Срок сдачи (дедлайн): `{dlFormatted}` ({urgency})\n");
        }

        sb.AppendLine(kind == TaskKind.Lecture ? "📄 *Материал:*" : "📝 *Задание:*");
        // Truncate long preview if necessary
        var previewText = taskText.Length > 800 ? taskText[..800] + "..." : taskText;
        sb.AppendLine(EscapeMarkdown(previewText));
        sb.AppendLine();
        if (attachmentCount > 0)
        {
            sb.AppendLine($"📎 Файлов: {attachmentCount}. Откройте задание в Mini App.");
            sb.AppendLine();
        }
        var baseText = sb.ToString();
        const string autoDeleteNote = "⏰ _Это сообщение автоматически удалится через 24 часа._";
        var deleteAt = DateTime.UtcNow.Add(SelfDestructDelay);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TelegramBotContext>();

        // Everyone's settings in two queries instead of one per user
        var settingsByUser = await db.NotificationSettings.AsNoTracking()
            .Where(s => targetUsers.Contains(s.UserId))
            .ToDictionaryAsync(s => s.UserId, cancellationToken);
        var mutedUsers = (await db.MutedSubjects.AsNoTracking()
                .Where(m => m.ChatGroupId == subjectId)
                .Select(m => m.UserId)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var mutedThisSubject = new HashSet<int> { subjectId };
        var localTime = LocalTime.NowTimeOfDay();

        int sentCount = 0, skipped = 0;

        foreach (var userId in targetUsers)
        {
            // The author does not need a notification about their own task
            if (userId == authorTelegramId) continue;

            settingsByUser.TryGetValue(userId, out var settings);
            var muted = mutedUsers.Contains(userId) ? mutedThisSubject : (IReadOnlySet<int>)new HashSet<int>();
            if (!NotificationPolicy.ShouldNotify(settings, muted, subjectId))
            {
                skipped++;
                continue;
            }

            var autoDelete = NotificationPolicy.ShouldAutoDelete(settings);

            try
            {
                var sentMsg = await _botClient.SendMessage(
                    chatId: userId,
                    text: autoDelete ? baseText + autoDeleteNote : baseText,
                    replyMarkup: replyMarkup,
                    parseMode: ParseMode.Markdown,
                    // Quiet hours: the message arrives without sound
                    disableNotification: NotificationPolicy.IsQuietTime(settings, localTime),
                    cancellationToken: cancellationToken
                );

                sentCount++;

                // Always remembered, so the notification disappears if the task itself is deleted.
                // Users who keep notifications get a "never" date for the 24-hour cleanup.
                db.SentNotifications.Add(new SentNotification
                {
                    TelegramChatId = userId,
                    TelegramMessageId = sentMsg.Id,
                    HomeworkMessageId = homeworkMessageId,
                    SentAt = DateTime.UtcNow,
                    DeleteAt = autoDelete ? deleteAt : KeepForever,
                    IsDeleted = false
                });

                // Throttling to respect Telegram API rate limits (max ~30 msgs/sec broadcast)
                await Task.Delay(50, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Не удалось отправить уведомление пользователю {UserId} (возможно, бот не был запущен)", userId);
            }
        }

        if (skipped > 0)
            _logger.LogInformation("ДЗ #{HomeworkId}: {Skipped} пользователей отключили эти уведомления", homeworkMessageId, skipped);

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

    private static string EscapeMarkdown(string text) => Bot.BotText.EscapeMarkdown(text);
}
