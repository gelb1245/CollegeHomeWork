using Microsoft.EntityFrameworkCore;
using System.Text.Json;
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
    private readonly IConfiguration _config;
    private readonly ILogger<TelegramBotPollingService> _logger;
    private readonly string _whitelistPath;
    private HashSet<long> _whitelist = new();

    // User ID of the primary developer/owner (Alexandr)
    private const long PrimaryOwnerId = 1645991235;

    // State for interactive commands: UserId -> CurrentAction
    private readonly Dictionary<long, (string Action, int GroupId)> _userStates = new();

    public TelegramBotPollingService(
        ITelegramBotClient botClient,
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        IWebHostEnvironment env,
        ILogger<TelegramBotPollingService> logger)
    {
        _botClient = botClient;
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
        _whitelistPath = Path.Combine(env.ContentRootPath, "whitelist.json");
        LoadWhitelist();
    }

    private void LoadWhitelist()
    {
        try
        {
            if (System.IO.File.Exists(_whitelistPath))
            {
                var json = System.IO.File.ReadAllText(_whitelistPath);
                _whitelist = JsonSerializer.Deserialize<HashSet<long>>(json) ?? new();
            }
            else
            {
                _whitelist = new();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка чтения whitelist.json");
            _whitelist = new();
        }

        // Always ensure PrimaryOwner is in whitelist
        _whitelist.Add(PrimaryOwnerId);
        SaveWhitelist();
    }

    private void SaveWhitelist()
    {
        try
        {
            var json = JsonSerializer.Serialize(_whitelist, new JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(_whitelistPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка записи whitelist.json");
        }
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
            var owner = await db.Users.FindAsync(new object[] { PrimaryOwnerId }, cancellationToken: stoppingToken);
            if (owner == null)
            {
                db.Users.Add(new AppUser
                {
                    TelegramId = PrimaryOwnerId,
                    Username = "Alexandr",
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
                    _ = ProcessUpdateAsync(update);
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

    private async Task ProcessUpdateAsync(Update update)
    {
        try
        {
            if (update.Type == UpdateType.Message && update.Message != null)
            {
                await HandleMessageAsync(update.Message);
            }
            else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
            {
                await HandleCallbackQueryAsync(update.CallbackQuery);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обработки обновления");
        }
    }

    private async Task HandleMessageAsync(Telegram.Bot.Types.Message message)
    {
        var from = message.From;
        if (from == null) return;
        long userId = from.Id;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TelegramBotContext>();

        var user = await db.Users.FindAsync(userId);

        // 1. Check if user is in whitelist
        if (!_whitelist.Contains(userId))
        {
            // Unauthorized user
            await _botClient.SendMessage(
                chatId: userId,
                text: $"⛔ *Доступ ограничен*\n\nВы не внесены в белый список колледжа.\nВаш ID: `{userId}`\n\nАдминистратор уже уведомлен о вашей попытке входа.",
                parseMode: ParseMode.Markdown
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
                chatId: PrimaryOwnerId,
                text: $"🔔 *Запрос доступа к боту:*\n• Имя: {from.FirstName} {from.LastName}\n• Ник: @{from.Username ?? "нет"}\n• Telegram ID: `{userId}`",
                replyMarkup: approveMarkup,
                parseMode: ParseMode.Markdown
            );
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
                Role = (userId == PrimaryOwnerId) ? RoleType.Developer : RoleType.User
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        if (userId == PrimaryOwnerId && user.Role != RoleType.Developer)
        {
            user.Role = RoleType.Developer;
            await db.SaveChangesAsync();
        }

        bool isPrivileged = user.Role == RoleType.Admin || user.Role == RoleType.Developer;

        // 2. Handle Contact sharing (Добавление из контактов)
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
                SaveWhitelist();

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
                // Contact does not have a public user ID
                await _botClient.SendMessage(
                    chatId: userId,
                    text: $"📱 Получен контакт *{contact.FirstName}* (`+{contact.PhoneNumber}`).\nУ контакта скрыт прямой Telegram ID. Попросите пользователя написать боту команду /start, и вы сможете подтвердить его в 1 клик!",
                    parseMode: ParseMode.Markdown
                );
            }
            return;
        }

        // 3. Handle forwarded message (добавление пересылкой)
        if (message.ForwardFrom != null && isPrivileged)
        {
            var forwardFrom = message.ForwardFrom;
            _whitelist.Add(forwardFrom.Id);
            SaveWhitelist();

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
        if (string.IsNullOrEmpty(text)) return;

        // 4. Handle text commands
        if (text.StartsWith("/start"))
        {
            await SendMainMenuAsync(userId, user, db);
            return;
        }

        if (text == "/menu" || text == "📚 Предметы и ДЗ")
        {
            await SendSubjectsListAsync(userId, db);
            return;
        }

        if (text == "👥 Вайтлист" || text == "/whitelist")
        {
            if (!isPrivileged)
            {
                await _botClient.SendMessage(userId, "У вас нет доступа к просмотру вайтлиста.");
                return;
            }
            await SendWhitelistInfoAsync(userId, db);
            return;
        }

        if (text.StartsWith("/add ") && isPrivileged)
        {
            var rawId = text.Substring(5).Trim();
            if (long.TryParse(rawId, out var targetId))
            {
                _whitelist.Add(targetId);
                SaveWhitelist();
                var u = await db.Users.FindAsync(targetId);
                if (u == null)
                {
                    db.Users.Add(new AppUser { TelegramId = targetId, Username = $"User_{targetId}", Role = RoleType.User });
                    await db.SaveChangesAsync();
                }
                await _botClient.SendMessage(userId, $"✅ Пользователь с ID `{targetId}` добавлен в вайтлист со статусом Студент.", parseMode: ParseMode.Markdown);
            }
            else
            {
                await _botClient.SendMessage(userId, "Использование: `/add <Telegram_ID>`", parseMode: ParseMode.Markdown);
            }
            return;
        }

        if (text.StartsWith("/del ") && isPrivileged)
        {
            var rawId = text.Substring(5).Trim();
            if (long.TryParse(rawId, out var targetId))
            {
                _whitelist.Remove(targetId);
                SaveWhitelist();
                await _botClient.SendMessage(userId, $"❌ Пользователь с ID `{targetId}` удален из вайтлиста.", parseMode: ParseMode.Markdown);
            }
            return;
        }

        if (text.StartsWith("/admin ") && user.Role == RoleType.Developer)
        {
            var rawId = text.Substring(7).Trim();
            if (long.TryParse(rawId, out var targetId))
            {
                var target = await db.Users.FindAsync(targetId);
                if (target != null)
                {
                    target.Role = RoleType.Admin;
                    await db.SaveChangesAsync();
                    await _botClient.SendMessage(userId, $"👑 Пользователь `{targetId}` назначен Администратором.");
                }
            }
            return;
        }

        if (text.StartsWith("/post ") && isPrivileged)
        {
            // /post <ID> <текст>
            var parts = text.Split(' ', 3);
            if (parts.Length == 3 && int.TryParse(parts[1], out var groupId))
            {
                var group = await db.ChatGroups.FindAsync(groupId);
                if (group != null)
                {
                    var msg = new AppMessage
                    {
                        ChatGroupId = groupId,
                        FromUserId = userId,
                        Text = parts[2],
                        SentAt = DateTime.UtcNow
                    };
                    db.Messages.Add(msg);
                    await db.SaveChangesAsync();
                    await _botClient.SendMessage(userId, $"✅ Домашнее задание опубликовано в предмет «*{group.Name}*»!", parseMode: ParseMode.Markdown);
                    return;
                }
            }
            await _botClient.SendMessage(userId, "Формат: `/post <ID_предмета> <текст задания>`", parseMode: ParseMode.Markdown);
            return;
        }

        // Check if user was waiting to type a message/homework
        if (_userStates.TryGetValue(userId, out var state))
        {
            if (state.Action == "post_msg")
            {
                var group = await db.ChatGroups.FindAsync(state.GroupId);
                if (group != null)
                {
                    var msg = new AppMessage
                    {
                        ChatGroupId = state.GroupId,
                        FromUserId = userId,
                        Text = text,
                        SentAt = DateTime.UtcNow
                    };
                    db.Messages.Add(msg);
                    await db.SaveChangesAsync();

                    _userStates.Remove(userId);
                    await _botClient.SendMessage(
                        chatId: userId,
                        text: $"✅ Задание успешно сохранено в предмет «*{group.Name}*»!\nВсе студенты теперь увидят его в боте и Mini App.",
                        parseMode: ParseMode.Markdown
                    );
                    return;
                }
            }
            else if (state.Action == "new_group" && user.Role == RoleType.Developer)
            {
                db.ChatGroups.Add(new ChatGroup { Name = text, Description = "Создано через Telegram" });
                await db.SaveChangesAsync();
                _userStates.Remove(userId);
                await _botClient.SendMessage(userId, $"📁 Предмет «*{text}*» успешно создан!", parseMode: ParseMode.Markdown);
                await SendSubjectsListAsync(userId, db);
                return;
            }
        }

        // Default answer
        await SendMainMenuAsync(userId, user, db);
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery query)
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

        // Group clicked -> Show Homework
        if (data.StartsWith("grp_"))
        {
            if (int.TryParse(data.Substring(4), out int groupId))
            {
                await ShowGroupHomeworkAsync(userId, groupId, db, isPrivileged);
            }
        }
        // Admin: Start posting homework
        else if (data.StartsWith("post_"))
        {
            if (int.TryParse(data.Substring(5), out int groupId) && isPrivileged)
            {
                var grp = await db.ChatGroups.FindAsync(groupId);
                _userStates[userId] = ("post_msg", groupId);
                await _botClient.SendMessage(
                    chatId: userId,
                    text: $"✍️ *Публикация задания в «{grp?.Name}»*\n\nОтправьте в чат текст домашнего задания или объявления (можно с ссылками и параграфами):",
                    parseMode: ParseMode.Markdown
                );
            }
        }
        // Developer: Add new subject
        else if (data == "add_group" && user.Role == RoleType.Developer)
        {
            _userStates[userId] = ("new_group", 0);
            await _botClient.SendMessage(userId, "✏️ Введите название нового предмета/чата (например, *Физика* или *Английский*):", parseMode: ParseMode.Markdown);
        }
        // Developer: Delete subject
        else if (data.StartsWith("delgrp_") && user.Role == RoleType.Developer)
        {
            if (int.TryParse(data.Substring(7), out int delId))
            {
                var g = await db.ChatGroups.FindAsync(delId);
                if (g != null)
                {
                    db.Messages.RemoveRange(db.Messages.Where(m => m.ChatGroupId == delId));
                    db.ChatGroups.Remove(g);
                    await db.SaveChangesAsync();
                    await _botClient.SendMessage(userId, $"🗑️ Предмет «{g.Name}» удалён.");
                    await SendSubjectsListAsync(userId, db);
                }
            }
        }
        // Whitelist Actions
        else if (data.StartsWith("wl_add_") && isPrivileged)
        {
            // wl_add_<targetId>_<role>
            var parts = data.Split('_');
            if (parts.Length == 4 && long.TryParse(parts[2], out long targetId))
            {
                var role = Enum.Parse<RoleType>(parts[3]);
                _whitelist.Add(targetId);
                SaveWhitelist();

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
                
                // Notify the student
                try
                {
                    await _botClient.SendMessage(targetId, "🎉 *Поздравляем!*\nАдминистратор одобрил ваш доступ к домашним заданиям. Нажмите /start для начала работы.", parseMode: ParseMode.Markdown);
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
                SaveWhitelist();
                await _botClient.SendMessage(userId, $"❌ Пользователь `{targetId}` удален из вайтлиста.", parseMode: ParseMode.Markdown);
            }
        }
        else if (data == "back_subjects")
        {
            await SendSubjectsListAsync(userId, db);
        }
    }

    private async Task SendMainMenuAsync(long userId, AppUser user, TelegramBotContext db)
    {
        string roleDesc = user.Role switch
        {
            RoleType.Developer => "👨‍💻 Разработчик (полный доступ)",
            RoleType.Admin => "👑 Администратор (публикация ДЗ)",
            _ => "🎓 Студент (просмотр ДЗ)"
        };

        var keyboardRows = new List<KeyboardButton[]>
        {
            new[] { new KeyboardButton("📚 Предметы и ДЗ") }
        };

        if (user.Role == RoleType.Admin || user.Role == RoleType.Developer)
        {
            keyboardRows.Add(new[]
            {
                new KeyboardButton("👥 Вайтлист"),
                KeyboardButton.WithRequestContact("➕ Добавить из контактов")
            });
        }

        var replyMarkup = new ReplyKeyboardMarkup(keyboardRows)
        {
            ResizeKeyboard = true
        };

        var webAppUrl = _config["Telegram:WebAppUrl"];
        var inlineRows = new List<InlineKeyboardButton[]>
        {
            new[] { InlineKeyboardButton.WithCallbackData("📖 Открыть список предметов", "back_subjects") }
        };

        if (!string.IsNullOrWhiteSpace(webAppUrl))
        {
            inlineRows.Add(new[] { InlineKeyboardButton.WithWebApp("📱 Открыть Mini App", new WebAppInfo { Url = webAppUrl }) });
        }

        await _botClient.SendMessage(
            chatId: userId,
            text: $"👋 *Добро пожаловать в бот ДЗ колледжа!*\n\n" +
                  $"👤 Ваш статус: *{roleDesc}*\n" +
                  $"• Нажмите *«📚 Предметы и ДЗ»*, чтобы посмотреть задания.\n" +
                  (user.Role != RoleType.User ? "• Чтобы добавить одногруппника, просто **отправьте его контакт** (скрепка 📎 -> Контакт) или перешлите от него сообщение!" : ""),
            replyMarkup: replyMarkup,
            parseMode: ParseMode.Markdown
        );

        await _botClient.SendMessage(
            chatId: userId,
            text: "Быстрые действия:",
            replyMarkup: new InlineKeyboardMarkup(inlineRows)
        );
    }

    private async Task SendSubjectsListAsync(long userId, TelegramBotContext db)
    {
        var groups = await db.ChatGroups.ToListAsync();
        var buttons = new List<InlineKeyboardButton[]>();

        foreach (var g in groups)
        {
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData($"📁 {g.Name}", $"grp_{g.Id}")
            });
        }

        var user = await db.Users.FindAsync(userId);
        if (user?.Role == RoleType.Developer)
        {
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData("➕ Добавить новый предмет", "add_group")
            });
        }

        var webAppUrl = _config["Telegram:WebAppUrl"];
        if (!string.IsNullOrWhiteSpace(webAppUrl))
        {
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithWebApp("📱 Открыть в Mini App", new WebAppInfo { Url = webAppUrl })
            });
        }

        await _botClient.SendMessage(
            chatId: userId,
            text: "📚 *Выберите предмет, чтобы посмотреть домашнее задание:*",
            replyMarkup: new InlineKeyboardMarkup(buttons),
            parseMode: ParseMode.Markdown
        );
    }

    private async Task ShowGroupHomeworkAsync(long userId, int groupId, TelegramBotContext db, bool isPrivileged)
    {
        var group = await db.ChatGroups.FindAsync(groupId);
        if (group == null) return;

        var messages = await db.Messages
            .Include(m => m.FromUser)
            .Where(m => m.ChatGroupId == groupId)
            .OrderByDescending(m => m.SentAt)
            .Take(10)
            .ToListAsync();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"📁 *Предмет: {group.Name}*");
        if (!string.IsNullOrEmpty(group.Description))
            sb.AppendLine($"_{group.Description}_\n");
        sb.AppendLine("────────────────────");

        if (messages.Count == 0)
        {
            sb.AppendLine("Пока нет опубликованных заданий по этому предмету.");
        }
        else
        {
            foreach (var m in messages)
            {
                string author = m.FromUser != null ? (m.FromUser.FullName ?? m.FromUser.Username) : "Администратор";
                string dateStr = m.SentAt.ToLocalTime().ToString("dd.MM.yyyy, HH:mm");
                sb.AppendLine($"✍️ *{author}* | 📅 `{dateStr}`");
                sb.AppendLine(m.Text);
                sb.AppendLine("────────────────────");
            }
        }

        var buttons = new List<InlineKeyboardButton[]>();
        if (isPrivileged)
        {
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData("✍️ Опубликовать новое ДЗ", $"post_{groupId}")
            });
        }

        var user = await db.Users.FindAsync(userId);
        if (user?.Role == RoleType.Developer)
        {
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData("🗑️ Удалить этот предмет", $"delgrp_{groupId}")
            });
        }

        buttons.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("⬅️ К списку предметов", "back_subjects")
        });

        await _botClient.SendMessage(
            chatId: userId,
            text: sb.ToString(),
            replyMarkup: new InlineKeyboardMarkup(buttons),
            parseMode: ParseMode.Markdown
        );
    }

    private async Task SendWhitelistInfoAsync(long userId, TelegramBotContext db)
    {
        var users = await db.Users.ToListAsync();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("👥 *Список пользователей (Вайтлист):*\n");

        foreach (var u in users)
        {
            string phone = !string.IsNullOrEmpty(u.PhoneNumber) ? $" (+{u.PhoneNumber})" : "";
            sb.AppendLine($"• *{u.FullName ?? u.Username}*{phone}");
            sb.AppendLine($"  ID: `{u.TelegramId}` | Роль: *{u.Role}*");
        }

        sb.AppendLine("\n💡 *Как добавить нового человека:*");
        sb.AppendLine("1. Отправьте контакт через скрепку 📎 -> Контакт");
        sb.AppendLine("2. Или перешлите сообщение от студента сюда");
        sb.AppendLine("3. Или введите команду: `/add <Telegram_ID>`");

        await _botClient.SendMessage(
            chatId: userId,
            text: sb.ToString(),
            parseMode: ParseMode.Markdown
        );
    }
}
