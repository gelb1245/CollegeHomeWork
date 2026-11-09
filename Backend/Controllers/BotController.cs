using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBotApi.Data;
using AppUser = TelegramBotApi.Models.User;
using AppMessage = TelegramBotApi.Models.Message;
using TelegramBotApi.Models;

namespace TelegramBotApi.Controllers;

[ApiController]
[Route("api/bot")]
public class BotController : ControllerBase
{
    private readonly ITelegramBotClient _botClient;
    private readonly TelegramBotContext _db;
    private readonly ILogger<BotController> _log;
    private readonly IConfiguration _config;
    private readonly HashSet<long> _whitelist;

    public BotController(ITelegramBotClient botClient, TelegramBotContext db, IWebHostEnvironment env, IConfiguration config, ILogger<BotController> log)
    {
        _botClient = botClient;
        _db = db;
        _config = config;
        _log = log;

        var path = Path.Combine(env.ContentRootPath, "whitelist.json");
        if (System.IO.File.Exists(path))
        {
            try
            {
                var json = System.IO.File.ReadAllText(path);
                _whitelist = JsonSerializer.Deserialize<HashSet<long>>(json) ?? new();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to read whitelist.json");
                _whitelist = new();
            }
        }
        else
        {
            _whitelist = new();
        }
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Update([FromBody] Update update)
    {
        var message = update.Message;
        if (message?.From?.Id is not { } userId) return Ok();

        // If whitelist has entries and userId is not in whitelist, reject
        if (_whitelist.Count > 0 && !_whitelist.Contains(userId))
        {
            _log.LogInformation("User {UserId} is not in whitelist", userId);
            return Ok();
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.TelegramId == userId);
        if (user == null)
        {
            user = new AppUser
            {
                TelegramId = userId,
                Username = message.From.Username ?? message.From.FirstName ?? "Unknown"
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();
        }

        var text = message.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return Ok();

        if (user.Role == RoleType.Admin && text.StartsWith("/post"))
        {
            // Expected: /post <groupId> <message>
            var parts = text.Split(' ', 3);
            if (parts.Length == 3 && int.TryParse(parts[1], out var groupId))
            {
                var group = await _db.ChatGroups.FindAsync(groupId);
                if (group != null)
                {
                    var msg = new AppMessage
                    {
                        ChatGroupId = groupId,
                        FromUserId = userId,
                        Text = parts[2],
                        SentAt = DateTime.UtcNow
                    };
                    _db.Messages.Add(msg);
                    await _db.SaveChangesAsync();
                    await _botClient.SendMessage(userId, $"✅ Сообщение опубликовано в группу «{group.Name}»");
                }
                else
                {
                    await _botClient.SendMessage(userId, "❌ Группа с таким ID не найдена.");
                }
            }
            else
            {
                await _botClient.SendMessage(userId, "Формат команды: /post <ID_группы> <текст>");
            }
        }
        else if (text == "/list")
        {
            var groups = await _db.ChatGroups.ToListAsync();
            if (groups.Count == 0)
            {
                await _botClient.SendMessage(userId, "Групп пока нет.");
            }
            else
            {
                var reply = "📚 Доступные группы:\n" + string.Join('\n', groups.Select(g => $"• ID {g.Id}: {g.Name}"));
                await _botClient.SendMessage(userId, reply);
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
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown
            );
        }

        return Ok();
    }
}
