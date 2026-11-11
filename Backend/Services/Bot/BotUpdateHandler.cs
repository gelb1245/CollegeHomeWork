using System.Text;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBotApi.Data;
using TelegramBotApi.Models;
using AppUser = TelegramBotApi.Models.User;

namespace TelegramBotApi.Services.Bot;

/// <summary>
/// Single entry point for all Telegram updates — used by both long polling and the webhook,
/// so the bot behaves the same in either mode.
/// Commands: /start (or any text) — Mini App button; /deadlines — upcoming deadlines.
/// Whitelist and role management is delegated to <see cref="BotAccessManager"/>.
/// </summary>
public class BotUpdateHandler
{
    private readonly ITelegramBotClient _bot;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WhitelistService _whitelist;
    private readonly TelegramRateLimiter _rateLimiter;
    private readonly BotAccessManager _access;
    private readonly IConfiguration _config;
    private readonly ILogger<BotUpdateHandler> _logger;

    // Limits how many updates are processed at the same time
    private readonly SemaphoreSlim _concurrency = new(10, 10);

    public BotUpdateHandler(
        ITelegramBotClient bot,
        IServiceScopeFactory scopeFactory,
        WhitelistService whitelist,
        TelegramRateLimiter rateLimiter,
        BotAccessManager access,
        IConfiguration config,
        ILogger<BotUpdateHandler> logger)
    {
        _bot = bot;
        _scopeFactory = scopeFactory;
        _whitelist = whitelist;
        _rateLimiter = rateLimiter;
        _access = access;
        _config = config;
        _logger = logger;
    }

    public async Task HandleAsync(Update update, CancellationToken ct)
    {
        try
        {
            await _concurrency.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (update.Message is { } message) await HandleMessageAsync(message, ct);
            else if (update.CallbackQuery is { } query) await _access.HandleCallbackAsync(query, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Ошибка обработки обновления Telegram #{UpdateId}", update.Id);
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private async Task HandleMessageAsync(Telegram.Bot.Types.Message message, CancellationToken ct)
    {
        if (message.From is not { } from) return;
        var userId = from.Id;

        // Anti-flood: ignore aggressive spam
        if (!_rateLimiter.CanProcessUserMessage(userId))
        {
            _logger.LogWarning("Сообщение от {UserId} проигнорировано (флуд)", userId);
            return;
        }

        if (!_whitelist.Contains(userId))
        {
            await _access.HandleUnauthorizedAsync(from, ct);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TelegramBotContext>();
        var user = await GetOrRegisterUserAsync(db, from, ct);
        var isPrivileged = user.Role is RoleType.Admin or RoleType.Developer;

        if (message.Contact != null)
        {
            await _access.HandleContactAsync(db, userId, isPrivileged, message.Contact, ct);
            return;
        }

        if (message.ForwardFrom != null && isPrivileged)
        {
            await _access.HandleForwardedAsync(db, userId, message.ForwardFrom, ct);
            return;
        }

        var text = message.Text?.Trim();
        if (text?.StartsWith("/deadlines", StringComparison.OrdinalIgnoreCase) == true || text == "⏳ Мои дедлайны")
        {
            await SendDeadlinesAsync(db, userId, ct);
            return;
        }

        await SendWelcomeAsync(user, ct);
    }

    private static async Task<AppUser> GetOrRegisterUserAsync(TelegramBotContext db, Telegram.Bot.Types.User from, CancellationToken ct)
    {
        var user = await db.Users.FindAsync(new object[] { from.Id }, ct);
        if (user == null)
        {
            user = new AppUser
            {
                TelegramId = from.Id,
                Username = from.Username ?? from.FirstName ?? "Студент",
                FullName = $"{from.FirstName} {from.LastName}".Trim(),
                Role = from.Id == WhitelistService.PrimaryOwnerId ? RoleType.Developer : RoleType.User
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }
        else if (from.Id == WhitelistService.PrimaryOwnerId && user.Role != RoleType.Developer)
        {
            user.Role = RoleType.Developer;
            await db.SaveChangesAsync(ct);
        }

        return user;
    }

    private async Task SendDeadlinesAsync(TelegramBotContext db, long userId, CancellationToken ct)
    {
        var upcoming = await db.Messages
            .AsNoTracking()
            .Where(m => m.Deadline != null)
            .OrderBy(m => m.Deadline)
            .Take(10)
            .Select(m => new
            {
                m.Text,
                Deadline = m.Deadline!.Value,
                GroupName = m.Group != null ? m.Group.Name : "Общее",
                IsDone = db.UserTaskProgresses.Any(p => p.MessageId == m.Id && p.UserId == userId && p.IsCompleted)
            })
            .ToListAsync(ct);

        if (upcoming.Count == 0)
        {
            await _bot.SendMessage(userId, "🎉 Нет активных дедлайнов! Все задания сданы или дедлайн не назначен.", cancellationToken: ct);
            return;
        }

        var sb = new StringBuilder("⏳ *Ваши актуальные задания и дедлайны:*\n\n");
        foreach (var m in upcoming)
        {
            var diff = m.Deadline - DateTime.UtcNow;
            var urgency = m.IsDone ? "✅ Сдано"
                : diff.TotalHours < 0 ? "🔥 Просрочено!"
                : diff.TotalHours < 24 ? "⚠️ Меньше 24ч!"
                : $"осталось {(int)diff.TotalDays} дн.";

            sb.AppendLine($"• *{BotText.EscapeMarkdown(m.GroupName)}*: {BotText.EscapeMarkdown(m.Text)}");
            sb.AppendLine($"   Срок: `{LocalTime.FromUtc(m.Deadline):dd.MM HH:mm}` ({urgency})\n");
        }
        sb.AppendLine($"📊 Выполнено: *{upcoming.Count(m => m.IsDone)} из {upcoming.Count}*");

        await _bot.SendMessage(
            chatId: userId,
            text: sb.ToString(),
            replyMarkup: MiniAppLink.Button(_config, "📱 Открыть задания в Mini App"),
            parseMode: ParseMode.Markdown,
            cancellationToken: ct);
    }

    private async Task SendWelcomeAsync(AppUser user, CancellationToken ct)
    {
        var roleDesc = user.Role switch
        {
            RoleType.Developer => "👨‍💻 Разработчик",
            RoleType.Admin => "👑 Администратор",
            _ => "🎓 Студент"
        };

        var appButton = MiniAppLink.Button(_config, "🚀 Открыть приложение предметов и ДЗ");

        var text = $"👋 Привет, *{BotText.EscapeMarkdown(user.FullName ?? user.Username)}*!\n\n" +
                   $"👤 Твой статус: *{roleDesc}*\n\n" +
                   "📲 Все задания, чаты предметов и управление — внутри *Mini App*!\n";

        text += appButton != null
            ? "Нажмите кнопку *«🚀 Открыть приложение»* ниже 👇"
            : "🌐 Публичный адрес Mini App не настроен (Telegram:WebAppUrl).";

        // Without a Mini App button, admins get a keyboard to share contacts into the whitelist
        IReplyMarkup? markup = appButton;
        if (markup == null && user.Role is RoleType.Admin or RoleType.Developer)
        {
            markup = new ReplyKeyboardMarkup(KeyboardButton.WithRequestContact("➕ Добавить контакт в вайтлист"))
            {
                ResizeKeyboard = true
            };
        }

        await _bot.SendMessage(user.TelegramId, text, parseMode: ParseMode.Markdown, replyMarkup: markup, cancellationToken: ct);
    }
}

/// <summary>Helpers for Telegram message text.</summary>
public static class BotText
{
    /// <summary>Escapes characters that have meaning in Telegram's legacy Markdown.</summary>
    public static string EscapeMarkdown(string? text) =>
        string.IsNullOrEmpty(text)
            ? string.Empty
            : text.Replace("_", "\\_").Replace("*", "\\*").Replace("[", "\\[").Replace("]", "\\]").Replace("`", "\\`");
}
