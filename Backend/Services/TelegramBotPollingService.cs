using System.Text;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBotApi.Data;
using TelegramBotApi.Models;
using AppUser = TelegramBotApi.Models.User;
using AppMessage = TelegramBotApi.Models.Message;

namespace TelegramBotApi.Services;

public class TelegramBotPollingService : BackgroundService
{
    private readonly ITelegramBotClient _botClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WhitelistService _whitelist;
    private readonly TelegramRateLimiter _rateLimiter;
    private readonly IConfiguration _config;
    private readonly ILogger<TelegramBotPollingService> _logger;
    private readonly SemaphoreSlim _concurrencySemaphore = new(10, 10);

    public TelegramBotPollingService(
        ITelegramBotClient botClient,
        IServiceScopeFactory scopeFactory,
        WhitelistService whitelist,
        TelegramRateLimiter rateLimiter,
        IConfiguration config,
        ILogger<TelegramBotPollingService> logger)
    {
        _botClient = botClient;
        _scopeFactory = scopeFactory;
        _whitelist = whitelist;
        _rateLimiter = rateLimiter;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Telegram Bot Polling Service запускается...");

        // Ensure database is initialized with default groups and primary admin
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TelegramBotContext>();
            await db.Database.EnsureCreatedAsync(stoppingToken);

            // Ensure owner is Developer
            var owner = await db.Users.FindAsync(new object[] { WhitelistService.PrimaryOwnerId }, cancellationToken: stoppingToken);
            if (owner == null)
            {
                db.Users.Add(new AppUser
                {
                    TelegramId = WhitelistService.PrimaryOwnerId,
                    Username = "Alexandr",
                    FullName = "Alexandr (Владелец)",
                    Role = RoleType.Developer
                });
            }
            else
            {
                owner.Role = RoleType.Developer;
            }

            // Ensure default subjects exist
            if (!await db.ChatGroups.AnyAsync(stoppingToken))
            {
                db.ChatGroups.AddRange(
                    new ChatGroup { Name = "История", Description = "Лекции и задания по Истории" },
                    new ChatGroup { Name = "Всесвит", Description = "Всемирная история и география" },
                    new ChatGroup { Name = "ДЗ Общее", Description = "Общие домашние задания и дедлайны" }
                );
            }

            await db.SaveChangesAsync(stoppingToken);
        }

        try
        {
            await _botClient.DropPendingUpdates(cancellationToken: stoppingToken);
            var me = await _botClient.GetMe(cancellationToken: stoppingToken);
            _logger.LogInformation("Бот успешно запущен: @{BotUsername} (ID: {BotId})", me.Username, me.Id);

            // Set Chat Menu Button to open Mini App if valid public HTTPS URL configured
            var webAppUrl = _config["Telegram:WebAppUrl"];
            if (IsValidTelegramWebAppUrl(webAppUrl))
            {
                try
                {
                    await _botClient.SetChatMenuButton(
                        menuButton: new MenuButtonWebApp
                        {
                            Text = "📱 Открыть ДЗ",
                            WebApp = new WebAppInfo { Url = webAppUrl! }
                        },
                        cancellationToken: stoppingToken
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Не удалось настроить Menu Button WebApp: {Error}", ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось подключиться к Telegram API. Проверьте токен бота.");
            return;
        }

        int offset = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await _botClient.GetUpdates(
                    offset: offset,
                    timeout: 25,
                    allowedUpdates: new[] { UpdateType.Message, UpdateType.CallbackQuery },
                    cancellationToken: stoppingToken);

                foreach (var update in updates)
                {
                    offset = update.Id + 1;
                    _ = ProcessUpdateAsync(update, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения обновлений Telegram");
                await Task.Delay(3000, stoppingToken);
            }
        }
    }

    private async Task ProcessUpdateAsync(Update update, CancellationToken cancellationToken)
    {
        try
        {
            await _concurrencySemaphore.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (update.Type == UpdateType.Message && update.Message != null)
            {
                await HandleMessageAsync(update.Message, cancellationToken);
            }
            else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
            {
                await HandleCallbackQueryAsync(update.CallbackQuery, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обработки обновления");
        }
        finally
        {
            _concurrencySemaphore.Release();
        }
    }

    private async Task HandleMessageAsync(Telegram.Bot.Types.Message message, CancellationToken cancellationToken)
    {
        var from = message.From;
        if (from == null) return;
        long userId = from.Id;

        // Anti-flood: Throttle aggressive spam / bot attacks
        if (!_rateLimiter.CanProcessUserMessage(userId))
        {
            _logger.LogWarning("Сообщение от пользователя {UserId} проигнорировано (превышен лимит запросов / флуд)", userId);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TelegramBotContext>();

        var user = await db.Users.FindAsync(new object[] { userId }, cancellationToken);

        // 1. Check if user is in whitelist
        if (!_whitelist.Contains(userId))
        {
            // Only notify owner if cooldown expired to prevent notification DDoS
            if (_rateLimiter.ShouldNotifyOwnerAboutUnauthorized(userId))
            {
                await _botClient.SendMessage(
                    chatId: userId,
                    text: $"⛔ *Доступ ограничен*\n\nВы не внесены в белый список колледжа.\nВаш ID: `{userId}`\n\nАдминистратор уже уведомлен о вашей попытке входа.",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken
                );

                // Notify Owner
                var approveMarkup = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("✅ Добавить студента", $"wl_add_{userId}_User"),
                        InlineKeyboardButton.WithCallbackData("👑 Сделать админом", $"wl_add_{userId}_Admin")
                    }
                });

                await _botClient.SendMessage(
                    chatId: WhitelistService.PrimaryOwnerId,
                    text: $"🔔 *Запрос доступа к боту:*\n• Имя: {from.FirstName} {from.LastName}\n• Ник: @{from.Username ?? "нет"}\n• Telegram ID: `{userId}`",
                    replyMarkup: approveMarkup,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken
                );
            }
            return;
        }

        // Register user in DB if not yet
        if (user == null)
        {
            user = new AppUser
            {
                TelegramId = userId,
                Username = from.Username ?? from.FirstName ?? "Студент",
                FullName = $"{from.FirstName} {from.LastName}".Trim(),
                Role = (userId == WhitelistService.PrimaryOwnerId) ? RoleType.Developer : RoleType.User
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        if (userId == WhitelistService.PrimaryOwnerId && user.Role != RoleType.Developer)
        {
            user.Role = RoleType.Developer;
            await db.SaveChangesAsync();
        }

        bool isPrivileged = user.Role == RoleType.Admin || user.Role == RoleType.Developer;

        // 2. Handle Contact sharing
        if (message.Contact != null)
        {
            if (!isPrivileged)
            {
                await _botClient.SendMessage(userId, "У вас нет прав на добавление контактов в вайтлист.");
                return;
            }

            var contact = message.Contact;
            if (contact.UserId.HasValue && contact.UserId.Value > 0)
            {
                long newUserId = contact.UserId.Value;
                _whitelist.Add(newUserId);

                var existingUser = await db.Users.FindAsync(newUserId);
                if (existingUser == null)
                {
                    db.Users.Add(new AppUser
                    {
                        TelegramId = newUserId,
                        Username = contact.FirstName,
                        FullName = $"{contact.FirstName} {contact.LastName}".Trim(),
                        PhoneNumber = contact.PhoneNumber,
                        Role = RoleType.User
                    });
                }
                else
                {
                    existingUser.PhoneNumber = contact.PhoneNumber;
                    existingUser.FullName = $"{contact.FirstName} {contact.LastName}".Trim();
                }
                await db.SaveChangesAsync();

                var roleButtons = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("👑 Сделать Админом", $"setrole_{newUserId}_Admin"),
                        InlineKeyboardButton.WithCallbackData("👤 Оставить Студентом", $"setrole_{newUserId}_User"),
                        InlineKeyboardButton.WithCallbackData("❌ Удалить", $"wl_del_{newUserId}")
                    }
                });

                await _botClient.SendMessage(
                    chatId: userId,
                    text: $"✅ *Контакт успешно добавлен в вайтлист!*\n\n• Имя: *{contact.FirstName} {contact.LastName}*\n• Телефон: `+{contact.PhoneNumber}`\n• Telegram ID: `{newUserId}`\n• Роль: *Студент*",
                    replyMarkup: roleButtons,
                    parseMode: ParseMode.Markdown
                );
            }
            else
            {
                await _botClient.SendMessage(
                    chatId: userId,
                    text: $"📱 Получен контакт *{contact.FirstName}* (`+{contact.PhoneNumber}`).\nУ контакта скрыт прямой Telegram ID. Попросите пользователя написать боту /start, и вы сможете подтвердить его в 1 клик!",
                    parseMode: ParseMode.Markdown
                );
            }
            return;
        }

        // 3. Handle forwarded message
        if (message.ForwardFrom != null && isPrivileged)
        {
            var forwardFrom = message.ForwardFrom;
            _whitelist.Add(forwardFrom.Id);

            var u = await db.Users.FindAsync(forwardFrom.Id);
            if (u == null)
            {
                db.Users.Add(new AppUser
                {
                    TelegramId = forwardFrom.Id,
                    Username = forwardFrom.Username ?? forwardFrom.FirstName,
                    FullName = $"{forwardFrom.FirstName} {forwardFrom.LastName}".Trim(),
                    Role = RoleType.User
                });
                await db.SaveChangesAsync();
            }

            var kb = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("👑 Сделать Админом", $"setrole_{forwardFrom.Id}_Admin"),
                    InlineKeyboardButton.WithCallbackData("❌ Убрать из вайтлиста", $"wl_del_{forwardFrom.Id}")
                }
            });

            await _botClient.SendMessage(
                chatId: userId,
                text: $"✅ *Студент добавлен в вайтлист из пересланного сообщения!*\n• Имя: {forwardFrom.FirstName}\n• Ник: @{forwardFrom.Username ?? "нет"}\n• ID: `{forwardFrom.Id}`",
                replyMarkup: kb,
                parseMode: ParseMode.Markdown
            );
            return;
        }

        var text = message.Text?.Trim();

        // 4. Handle /deadlines command
        if (text?.StartsWith("/deadlines", StringComparison.OrdinalIgnoreCase) == true || text == "⏳ Мои дедлайны")
        {
            var upcoming = await db.Messages
                .AsNoTracking()
                .Include(m => m.Group)
                .Where(m => m.Deadline != null)
                .OrderBy(m => m.Deadline)
                .Take(10)
                .ToListAsync(cancellationToken);

            if (upcoming.Count == 0)
            {
                await _botClient.SendMessage(userId, "🎉 Нет активных дедлайнов! Все задания сданы или дедлайн не назначен.", cancellationToken: cancellationToken);
                return;
            }

            var messageIds = upcoming.Select(m => m.Id).ToList();
            var completedIds = await db.UserTaskProgresses
                .AsNoTracking()
                .Where(p => p.UserId == userId && messageIds.Contains(p.MessageId) && p.IsCompleted)
                .Select(p => p.MessageId)
                .ToListAsync(cancellationToken);

            var sb = new StringBuilder("⏳ *Ваши актуальные задания и дедлайны:*\n\n");
            int total = upcoming.Count;
            int done = completedIds.Count;

            foreach (var m in upcoming)
            {
                bool isDone = completedIds.Contains(m.Id);
                var dl = m.Deadline!.Value;
                var dlStr = dl.ToString("dd.MM HH:mm");
                var diff = dl - DateTime.UtcNow;
                string urgency = isDone ? "✅ Сдано" :
                                 diff.TotalHours < 0 ? "🔥 Просрочено!" :
                                 diff.TotalHours < 24 ? "⚠️ Меньше 24ч!" :
                                 $"осталось {(int)diff.TotalDays} дн.";

                sb.AppendLine($"• *{m.Group?.Name}*: {m.Text}");
                sb.AppendLine($"   Срок: `{dlStr}` ({urgency})\n");
            }

            sb.AppendLine($"📊 Выполнено: *{done} из {total}*");

            var webAppUrl = _config["Telegram:WebAppUrl"];
            InlineKeyboardMarkup? ikb = null;
            if (IsValidTelegramWebAppUrl(webAppUrl))
            {
                ikb = new InlineKeyboardMarkup(new[]
                {
                    InlineKeyboardButton.WithWebApp("📱 Открыть задания в Mini App", new WebAppInfo { Url = webAppUrl! })
                });
            }

            await _botClient.SendMessage(
                chatId: userId,
                text: sb.ToString(),
                replyMarkup: ikb,
                parseMode: ParseMode.Markdown,
                cancellationToken: cancellationToken
            );
            return;
        }

        // Send Mini App button & menu
        await SendAppWelcomeAsync(userId, user);
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery query, CancellationToken cancellationToken = default)
    {
        var data = query.Data;
        if (string.IsNullOrEmpty(data)) return;
        long userId = query.From.Id;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TelegramBotContext>();
        var user = await db.Users.FindAsync(userId);
        if (user == null || !_whitelist.Contains(userId)) return;

        bool isPrivileged = user.Role == RoleType.Admin || user.Role == RoleType.Developer;

        await _botClient.AnswerCallbackQuery(query.Id);

        if (data.StartsWith("wl_add_") && isPrivileged)
        {
            var parts = data.Split('_');
            if (parts.Length == 4 && long.TryParse(parts[2], out long targetId))
            {
                var role = Enum.Parse<RoleType>(parts[3]);
                _whitelist.Add(targetId);

                var targetUser = await db.Users.FindAsync(targetId);
                if (targetUser == null)
                {
                    targetUser = new AppUser { TelegramId = targetId, Username = $"User_{targetId}", Role = role };
                    db.Users.Add(targetUser);
                }
                else
                {
                    targetUser.Role = role;
                }
                await db.SaveChangesAsync();

                await _botClient.SendMessage(userId, $"✅ Пользователь `{targetId}` добавлен в вайтлист с ролью *{role}*!", parseMode: ParseMode.Markdown);

                try
                {
                    var webAppUrl = _config["Telegram:WebAppUrl"];
                    var buttons = new List<InlineKeyboardButton[]>();
                    if (!string.IsNullOrWhiteSpace(webAppUrl))
                    {
                        buttons.Add(new[] { InlineKeyboardButton.WithWebApp("📱 Открыть приложение ДЗ", new WebAppInfo { Url = webAppUrl }) });
                    }
                    await _botClient.SendMessage(
                        chatId: targetId,
                        text: "🎉 *Доступ одобрен!*\nАдминистратор открыл вам доступ. Откройте приложение ниже:",
                        replyMarkup: buttons.Count > 0 ? new InlineKeyboardMarkup(buttons) : null,
                        parseMode: ParseMode.Markdown
                    );
                }
                catch { }
            }
        }
        else if (data.StartsWith("setrole_") && user.Role == RoleType.Developer)
        {
            var parts = data.Split('_');
            if (parts.Length == 3 && long.TryParse(parts[1], out long targetId))
            {
                var role = Enum.Parse<RoleType>(parts[2]);
                var targetUser = await db.Users.FindAsync(targetId);
                if (targetUser != null)
                {
                    targetUser.Role = role;
                    await db.SaveChangesAsync();
                    await _botClient.SendMessage(userId, $"Роль пользователя `{targetId}` изменена на *{role}*.", parseMode: ParseMode.Markdown);
                }
            }
        }
        else if (data.StartsWith("wl_del_") && isPrivileged)
        {
            if (long.TryParse(data.Substring(7), out long targetId))
            {
                _whitelist.Remove(targetId);
                var targetUser = await db.Users.FindAsync(targetId);
                if (targetUser != null && targetId != WhitelistService.PrimaryOwnerId)
                {
                    db.Users.Remove(targetUser);
                    await db.SaveChangesAsync();
                }
                await _botClient.SendMessage(userId, $"❌ Пользователь `{targetId}` удален из вайтлиста.", parseMode: ParseMode.Markdown);
            }
        }
    }

    private static bool IsValidTelegramWebAppUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
        if (url.Contains("localhost", StringComparison.OrdinalIgnoreCase)) return false;
        if (url.Contains("127.0.0.1")) return false;
        return true;
    }

    private async Task SendAppWelcomeAsync(long userId, AppUser user)
    {
        string roleDesc = user.Role switch
        {
            RoleType.Developer => "👨‍💻 Разработчик",
            RoleType.Admin => "👑 Администратор",
            _ => "🎓 Студент"
        };

        var webAppUrl = _config["Telegram:WebAppUrl"];
        var inlineButtons = new List<InlineKeyboardButton[]>();

        if (IsValidTelegramWebAppUrl(webAppUrl))
        {
            inlineButtons.Add(new[]
            {
                InlineKeyboardButton.WithWebApp("🚀 Открыть приложение предметов и ДЗ", new WebAppInfo { Url = webAppUrl! })
            });
        }

        var keyboardMarkup = new ReplyKeyboardMarkup(new[]
        {
            new[] { new KeyboardButton("📱 Открыть Mini App") },
            (user.Role == RoleType.Developer || user.Role == RoleType.Admin)
                ? new[] { KeyboardButton.WithRequestContact("➕ Добавить контакт в вайтлист") }
                : Array.Empty<KeyboardButton>()
        }.Where(r => r.Length > 0))
        {
            ResizeKeyboard = true
        };

        string welcomeText = $"👋 Привет, *{user.FullName ?? user.Username}*!\n\n" +
                             $"👤 Твой статус: *{roleDesc}*\n\n" +
                             $"📲 Все задания, чаты предметов и управление происходят внутри **Mini App**!\n";

        if (IsValidTelegramWebAppUrl(webAppUrl))
        {
            welcomeText += "Нажмите кнопку **«🚀 Открыть приложение»** ниже, чтобы войти 👇";
        }
        else
        {
            welcomeText += $"🌐 Локальный адрес для входа в браузере:\n`http://localhost:5000`\n\n" +
                           $"💡 Для того чтобы кнопка открывалась прямо внутри Telegram, подключите публичный URL (например, бесплатный хостинг Render).";
        }

        await _botClient.SendMessage(
            chatId: userId,
            text: welcomeText,
            replyMarkup: inlineButtons.Count > 0 ? new InlineKeyboardMarkup(inlineButtons) : keyboardMarkup,
            parseMode: ParseMode.Markdown
        );
    }
}
