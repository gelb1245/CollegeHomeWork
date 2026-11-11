using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBotApi.Data;
using TelegramBotApi.Models;
using AppUser = TelegramBotApi.Models.User;

namespace TelegramBotApi.Services.Bot;

/// <summary>
/// Whitelist and role management through the bot:
/// access requests from unknown users, shared contacts, forwarded messages and the inline buttons
/// ("add student", "make admin", "remove").
/// Callback data format: wl_add_{id}_{Role} · setrole_{id}_{Role} · wl_del_{id}
/// </summary>
public class BotAccessManager
{
    private readonly ITelegramBotClient _bot;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WhitelistService _whitelist;
    private readonly TelegramRateLimiter _rateLimiter;
    private readonly IConfiguration _config;

    public BotAccessManager(
        ITelegramBotClient bot,
        IServiceScopeFactory scopeFactory,
        WhitelistService whitelist,
        TelegramRateLimiter rateLimiter,
        IConfiguration config)
    {
        _bot = bot;
        _scopeFactory = scopeFactory;
        _whitelist = whitelist;
        _rateLimiter = rateLimiter;
        _config = config;
    }

    /// <summary>A user outside the whitelist wrote to the bot: tell them their ID and ask the owner.</summary>
    public async Task HandleUnauthorizedAsync(Telegram.Bot.Types.User from, CancellationToken ct)
    {
        // Cooldown so that spam does not flood the owner with requests
        if (!_rateLimiter.ShouldNotifyOwnerAboutUnauthorized(from.Id)) return;

        await _bot.SendMessage(
            chatId: from.Id,
            text: $"⛔ *Доступ ограничен*\n\nВы не внесены в белый список колледжа.\nВаш ID: `{from.Id}`\n\nАдминистратор уже уведомлен о вашей попытке входа.",
            parseMode: ParseMode.Markdown,
            cancellationToken: ct);

        var approve = new InlineKeyboardMarkup(new[]
        {
            InlineKeyboardButton.WithCallbackData("✅ Добавить студента", $"wl_add_{from.Id}_User"),
            InlineKeyboardButton.WithCallbackData("👑 Сделать админом", $"wl_add_{from.Id}_Admin")
        });

        await _bot.SendMessage(
            chatId: WhitelistService.PrimaryOwnerId,
            text: $"🔔 *Запрос доступа к боту:*\n• Имя: {BotText.EscapeMarkdown($"{from.FirstName} {from.LastName}".Trim())}\n" +
                  $"• Ник: @{BotText.EscapeMarkdown(from.Username ?? "нет")}\n• Telegram ID: `{from.Id}`",
            replyMarkup: approve,
            parseMode: ParseMode.Markdown,
            cancellationToken: ct);
    }

    /// <summary>An admin shared a contact: add that person to the whitelist.</summary>
    public async Task HandleContactAsync(TelegramBotContext db, long adminId, bool isPrivileged, Contact contact, CancellationToken ct)
    {
        if (!isPrivileged)
        {
            await _bot.SendMessage(adminId, "У вас нет прав на добавление контактов в вайтлист.", cancellationToken: ct);
            return;
        }

        var fullName = $"{contact.FirstName} {contact.LastName}".Trim();

        if (contact.UserId is not { } newUserId || newUserId <= 0)
        {
            await _bot.SendMessage(
                chatId: adminId,
                text: $"📱 Получен контакт *{BotText.EscapeMarkdown(contact.FirstName)}*.\nУ контакта скрыт Telegram ID. Попросите его написать боту /start — вы получите кнопку подтверждения.",
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
            return;
        }

        _whitelist.Add(newUserId);

        var existing = await db.Users.FindAsync(new object[] { newUserId }, ct);
        if (existing == null)
        {
            db.Users.Add(new AppUser
            {
                TelegramId = newUserId,
                Username = contact.FirstName,
                FullName = fullName,
                PhoneNumber = contact.PhoneNumber,
                Role = RoleType.User
            });
        }
        else
        {
            existing.PhoneNumber = contact.PhoneNumber;
            existing.FullName = fullName;
        }
        await db.SaveChangesAsync(ct);

        await _bot.SendMessage(
            chatId: adminId,
            text: $"✅ *Контакт добавлен в вайтлист!*\n\n• Имя: *{BotText.EscapeMarkdown(fullName)}*\n• Telegram ID: `{newUserId}`\n• Роль: *Студент*",
            replyMarkup: RoleButtons(newUserId),
            parseMode: ParseMode.Markdown,
            cancellationToken: ct);
    }

    /// <summary>An admin forwarded someone's message: add the original sender to the whitelist.</summary>
    public async Task HandleForwardedAsync(TelegramBotContext db, long adminId, Telegram.Bot.Types.User forwardFrom, CancellationToken ct)
    {
        _whitelist.Add(forwardFrom.Id);

        if (await db.Users.FindAsync(new object[] { forwardFrom.Id }, ct) == null)
        {
            db.Users.Add(new AppUser
            {
                TelegramId = forwardFrom.Id,
                Username = forwardFrom.Username ?? forwardFrom.FirstName,
                FullName = $"{forwardFrom.FirstName} {forwardFrom.LastName}".Trim(),
                Role = RoleType.User
            });
            await db.SaveChangesAsync(ct);
        }

        await _bot.SendMessage(
            chatId: adminId,
            text: $"✅ *Студент добавлен в вайтлист из пересланного сообщения!*\n• Имя: {BotText.EscapeMarkdown(forwardFrom.FirstName)}\n" +
                  $"• Ник: @{BotText.EscapeMarkdown(forwardFrom.Username ?? "нет")}\n• ID: `{forwardFrom.Id}`",
            replyMarkup: RoleButtons(forwardFrom.Id),
            parseMode: ParseMode.Markdown,
            cancellationToken: ct);
    }

    /// <summary>Inline button pressed (whitelist / role actions).</summary>
    public async Task HandleCallbackAsync(CallbackQuery query, CancellationToken ct)
    {
        var data = query.Data;
        if (string.IsNullOrEmpty(data)) return;
        var adminId = query.From.Id;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TelegramBotContext>();

        var admin = await db.Users.FindAsync(new object[] { adminId }, ct);
        if (admin == null || !_whitelist.Contains(adminId)) return;
        var isPrivileged = admin.Role is RoleType.Admin or RoleType.Developer;

        await _bot.AnswerCallbackQuery(query.Id, cancellationToken: ct);

        var parts = data.Split('_');
        if (data.StartsWith("wl_add_") && isPrivileged && parts.Length == 4 &&
            long.TryParse(parts[2], out var addId) && Enum.TryParse<RoleType>(parts[3], out var addRole) && addRole != RoleType.Developer)
        {
            await AddToWhitelistAsync(db, adminId, addId, addRole, ct);
        }
        else if (data.StartsWith("setrole_") && admin.Role == RoleType.Developer && parts.Length == 3 &&
                 long.TryParse(parts[1], out var roleId) && Enum.TryParse<RoleType>(parts[2], out var newRole))
        {
            await SetRoleAsync(db, adminId, roleId, newRole, ct);
        }
        else if (data.StartsWith("wl_del_") && isPrivileged && long.TryParse(data["wl_del_".Length..], out var delId))
        {
            await RemoveFromWhitelistAsync(db, adminId, delId, ct);
        }
    }

    private async Task AddToWhitelistAsync(TelegramBotContext db, long adminId, long targetId, RoleType role, CancellationToken ct)
    {
        _whitelist.Add(targetId);

        var target = await db.Users.FindAsync(new object[] { targetId }, ct);
        if (target == null) db.Users.Add(new AppUser { TelegramId = targetId, Username = $"User_{targetId}", Role = role });
        else target.Role = role;
        await db.SaveChangesAsync(ct);

        await _bot.SendMessage(adminId, $"✅ Пользователь `{targetId}` добавлен в вайтлист с ролью *{role}*!",
            parseMode: ParseMode.Markdown, cancellationToken: ct);

        try
        {
            await _bot.SendMessage(
                chatId: targetId,
                text: "🎉 *Доступ одобрен!*\nАдминистратор открыл вам доступ. Откройте приложение:",
                replyMarkup: MiniAppLink.Button(_config, "📱 Открыть приложение ДЗ"),
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
        }
        catch (Exception)
        {
            // The user may have blocked the bot — access is granted anyway
        }
    }

    private async Task SetRoleAsync(TelegramBotContext db, long adminId, long targetId, RoleType role, CancellationToken ct)
    {
        if (targetId == WhitelistService.PrimaryOwnerId && role != RoleType.Developer)
        {
            await _bot.SendMessage(adminId, "Роль владельца изменить нельзя.", cancellationToken: ct);
            return;
        }

        var target = await db.Users.FindAsync(new object[] { targetId }, ct);
        if (target == null) return;

        target.Role = role;
        await db.SaveChangesAsync(ct);
        await _bot.SendMessage(adminId, $"Роль пользователя `{targetId}` изменена на *{role}*.",
            parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    private async Task RemoveFromWhitelistAsync(TelegramBotContext db, long adminId, long targetId, CancellationToken ct)
    {
        if (targetId == WhitelistService.PrimaryOwnerId)
        {
            await _bot.SendMessage(adminId, "Владельца нельзя удалить из вайтлиста.", cancellationToken: ct);
            return;
        }

        _whitelist.Remove(targetId);
        var target = await db.Users.FindAsync(new object[] { targetId }, ct);
        if (target != null)
        {
            db.Users.Remove(target);
            await db.SaveChangesAsync(ct);
        }
        await _bot.SendMessage(adminId, $"❌ Пользователь `{targetId}` удалён из вайтлиста.",
            parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    private static InlineKeyboardMarkup RoleButtons(long userId) => new(new[]
    {
        InlineKeyboardButton.WithCallbackData("👑 Сделать админом", $"setrole_{userId}_Admin"),
        InlineKeyboardButton.WithCallbackData("❌ Убрать", $"wl_del_{userId}")
    });
}
