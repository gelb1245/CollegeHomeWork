using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBotApi.Data;
using TelegramBotApi.Models;
using TelegramBotApi.Services;
using AppUser = TelegramBotApi.Models.User;
using AppMessage = TelegramBotApi.Models.Message;

namespace TelegramBotApi.Controllers;

[ApiController]
[Route("api/bot")]
[EnableRateLimiting("strict-api")]
public class BotController : ControllerBase
{
    private readonly ITelegramBotClient _botClient;
    private readonly TelegramBotContext _db;
    private readonly WhitelistService _whitelist;
    private readonly TelegramRateLimiter _rateLimiter;
    private readonly HomeworkNotificationService _notifications;
    private readonly ILogger<BotController> _log;
    private readonly IConfiguration _config;

    public BotController(
        ITelegramBotClient botClient,
        TelegramBotContext db,
        WhitelistService whitelist,
        TelegramRateLimiter rateLimiter,
        HomeworkNotificationService notifications,
        IConfiguration config,
        ILogger<BotController> log)
    {
        _botClient = botClient;
        _db = db;
        _whitelist = whitelist;
        _rateLimiter = rateLimiter;
        _notifications = notifications;
        _config = config;
        _log = log;
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Update([FromBody] Update? update, CancellationToken cancellationToken)
    {
        // 1. Verify Webhook Secret Token if configured to prevent spoofed/unauthorized updates
        var expectedSecret = _config["Telegram:WebhookSecretToken"];
        if (!string.IsNullOrWhiteSpace(expectedSecret))
        {
            if (!Request.Headers.TryGetValue("X-Telegram-Bot-Api-Secret-Token", out var receivedSecret) ||
                receivedSecret != expectedSecret)
            {
                _log.LogWarning("Webhook запрос отклонён: неверный X-Telegram-Bot-Api-Secret-Token.");
                return StatusCode(StatusCodes.Status403Forbidden, "Forbidden");
            }
        }

        if (update?.Message is not { } message || message.From?.Id is not { } userId)
        {
            return Ok();
        }

        // 2. Anti-flood rate limiting
        if (!_rateLimiter.CanProcessUserMessage(userId))
        {
            _log.LogWarning("Webhook update от пользователя {UserId} проигнорирован (флуд)", userId);
            return Ok();
        }

        // 3. Whitelist verification
        if (!_whitelist.Contains(userId))
        {
            _log.LogInformation("Пользователь {UserId} отсутствует в вайтлисте", userId);
            return Ok();
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == userId, cancellationToken);
        if (user == null)
        {
            user = new AppUser
            {
                TelegramId = userId,
                Username = message.From.Username ?? message.From.FirstName ?? "Unknown"
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var text = message.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return Ok();

        if ((user.Role == RoleType.Admin || user.Role == RoleType.Developer) && text.StartsWith("/post"))
        {
            var parts = text.Split(' ', 3);
            if (parts.Length == 3 && int.TryParse(parts[1], out var groupId))
            {
                var group = await _db.ChatGroups.FindAsync(new object[] { groupId }, cancellationToken);
                if (group != null)
                {
                    var msg = new AppMessage
                    {
                        ChatGroupId = groupId,
                        FromUserId = userId,
                        Text = parts[2].Trim(),
                        SentAt = DateTime.UtcNow
                    };
                    _db.Messages.Add(msg);
                    await _db.SaveChangesAsync(cancellationToken);

                    // Dispatch self-destructing notifications (deleted after 7 hours)
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _notifications.NotifyStudentsAsync(
                                msg.Id,
                                group.Name,
                                msg.Text,
                                msg.Deadline,
                                userId
                            );
                        }
                        catch (Exception ex)
                        {
                            _log.LogError(ex, "Ошибка при рассылке уведомлений о ДЗ #{Id}", msg.Id);
                        }
                    });

                    await _botClient.SendMessage(userId, $"✅ Сообщение опубликовано в группу «{group.Name}»", cancellationToken: cancellationToken);
                }
                else
                {
                    await _botClient.SendMessage(userId, "❌ Группа с таким ID не найдена.", cancellationToken: cancellationToken);
                }
            }
            else
            {
                await _botClient.SendMessage(userId, "Формат команды: /post <ID_группы> <текст>", cancellationToken: cancellationToken);
            }
        }
        else if (text == "/list")
        {
            var groups = await _db.ChatGroups.AsNoTracking().ToListAsync(cancellationToken);
            if (groups.Count == 0)
            {
                await _botClient.SendMessage(userId, "Групп пока нет.", cancellationToken: cancellationToken);
            }
            else
            {
                var reply = "📚 Доступные группы:\n" + string.Join('\n', groups.Select(g => $"• ID {g.Id}: {g.Name}"));
                await _botClient.SendMessage(userId, reply, cancellationToken: cancellationToken);
            }
        }
        else if (text == "/start")
        {
            var webAppUrl = _config["Telegram:WebAppUrl"];
            InlineKeyboardMarkup? keyboard = null;
            if (!string.IsNullOrWhiteSpace(webAppUrl))
            {
                keyboard = new InlineKeyboardMarkup(new[]
                {
                    InlineKeyboardButton.WithWebApp("📱 Открыть ДЗ и Чаты", new WebAppInfo { Url = webAppUrl })
                });
            }

            await _botClient.SendMessage(
                chatId: userId,
                text: $"👋 Привет, {user.Username}!\n\nТвоя роль: *{user.Role}*\n• Отправь /list для просмотра предметов\n• Если ты администратор, публикуй ДЗ: `/post <ID_группы> <текст>`",
                replyMarkup: keyboard,
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                cancellationToken: cancellationToken
            );
        }

        return Ok();
    }
}
