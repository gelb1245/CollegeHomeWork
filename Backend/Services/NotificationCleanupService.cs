using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using TelegramBotApi.Data;

namespace TelegramBotApi.Services;

/// <summary>
/// Background worker that periodically checks for sent notification messages that reached their 24-hour expiration
/// and deletes them from users' Telegram chats.
/// </summary>
public class NotificationCleanupService : BackgroundService
{
    private readonly ITelegramBotClient _botClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationCleanupService> _logger;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    public NotificationCleanupService(
        ITelegramBotClient botClient,
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationCleanupService> logger)
    {
        _botClient = botClient;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Сервис автоудаления уведомлений (через 24 часа) запущен.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessExpiredNotificationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке автоудаления уведомлений");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Сервис автоудаления уведомлений остановлен.");
    }

    private async Task ProcessExpiredNotificationsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TelegramBotContext>();

        var now = DateTime.UtcNow;

        // Fetch up to 50 expired notifications that haven't been deleted yet
        var expiredList = await db.SentNotifications
            .Where(n => !n.IsDeleted && n.DeleteAt <= now)
            .OrderBy(n => n.DeleteAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (expiredList.Count == 0) return;

        int deletedCount = 0;

        foreach (var notification in expiredList)
        {
            try
            {
                await _botClient.DeleteMessage(
                    chatId: notification.TelegramChatId,
                    messageId: notification.TelegramMessageId,
                    cancellationToken: cancellationToken
                );
                deletedCount++;
            }
            catch (Exception ex)
            {
                // Message might have been deleted by the user or bot was blocked
                _logger.LogDebug(ex, "Сообщение {MsgId} в чате {ChatId} уже недоступно для удаления",
                    notification.TelegramMessageId, notification.TelegramChatId);
            }

            notification.IsDeleted = true;

            // Small delay between Telegram API calls to stay within rate limits
            await Task.Delay(40, cancellationToken);
        }

        // Clean up archived records older than 7 days to keep SQLite light
        var archiveCutoff = now.AddDays(-7);
        var oldRecords = await db.SentNotifications
            .Where(n => n.IsDeleted && n.SentAt <= archiveCutoff)
            .Take(100)
            .ToListAsync(cancellationToken);

        if (oldRecords.Count > 0)
        {
            db.SentNotifications.RemoveRange(oldRecords);
        }

        await db.SaveChangesAsync(cancellationToken);

        if (deletedCount > 0)
        {
            _logger.LogInformation("Удалено {Count} уведомлений из Telegram (истекли 24 часа)", deletedCount);
        }
    }
}
