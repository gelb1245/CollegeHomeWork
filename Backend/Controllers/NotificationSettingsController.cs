using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using TelegramBotApi.Attributes;
using TelegramBotApi.Data;
using TelegramBotApi.Models;
using TelegramBotApi.Services;
using TelegramBotApi.Services.Bot;

namespace TelegramBotApi.Controllers;

public record SubjectMuteDto(int Id, string Name, bool Muted);

public record NotificationSettingsDto(
    bool NewTasks,
    bool AutoDelete,
    bool QuietHoursEnabled,
    string QuietFrom,
    string QuietTo,
    IReadOnlyList<SubjectMuteDto> Subjects);

public record UpdateNotificationSettingsDto(
    bool NewTasks,
    bool AutoDelete,
    bool QuietHoursEnabled,
    string QuietFrom,
    string QuietTo,
    List<int>? MutedSubjectIds);

/// <summary>
/// Personal notification settings of the current user (any role).
/// </summary>
[ApiController]
[Route("api/admin/notification-settings")]
[EnableRateLimiting("strict-api")]
[TelegramAuthorize]
public class NotificationSettingsController : ControllerBase
{
    private readonly TelegramBotContext _db;
    private readonly ITelegramBotClient _bot;
    private readonly IConfiguration _config;
    private readonly ILogger<NotificationSettingsController> _log;

    private User CurrentUser => (User)HttpContext.Items["CurrentUser"]!;

    public NotificationSettingsController(TelegramBotContext db, ITelegramBotClient bot, IConfiguration config,
        ILogger<NotificationSettingsController> log)
    {
        _db = db;
        _bot = bot;
        _config = config;
        _log = log;
    }

    [HttpGet]
    public async Task<ActionResult<NotificationSettingsDto>> Get(CancellationToken ct) => Ok(await LoadAsync(ct));

    [HttpPut]
    public async Task<ActionResult<NotificationSettingsDto>> Update([FromBody] UpdateNotificationSettingsDto dto, CancellationToken ct)
    {
        if (!NotificationPolicy.TryParseTime(dto.QuietFrom, out var from) ||
            !NotificationPolicy.TryParseTime(dto.QuietTo, out var to))
        {
            return BadRequest(new { error = "Время тихих часов должно быть в формате ЧЧ:ММ." });
        }

        var userId = CurrentUser.TelegramId;
        var settings = await _db.NotificationSettings.FindAsync(new object[] { userId }, ct);
        if (settings == null)
        {
            settings = new NotificationSettings { UserId = userId };
            _db.NotificationSettings.Add(settings);
        }
        settings.NewTasks = dto.NewTasks;
        settings.AutoDelete = dto.AutoDelete;
        settings.QuietHoursEnabled = dto.QuietHoursEnabled;
        settings.QuietFromMinutes = from;
        settings.QuietToMinutes = to;

        // Replace the muted subjects (only existing subjects are accepted)
        var requested = (dto.MutedSubjectIds ?? new List<int>()).Distinct().ToList();
        var validIds = await _db.ChatGroups.Where(g => requested.Contains(g.Id)).Select(g => g.Id).ToListAsync(ct);
        var current = await _db.MutedSubjects.Where(m => m.UserId == userId).ToListAsync(ct);
        _db.MutedSubjects.RemoveRange(current.Where(m => !validIds.Contains(m.ChatGroupId)));
        _db.MutedSubjects.AddRange(validIds
            .Where(id => current.All(m => m.ChatGroupId != id))
            .Select(id => new MutedSubject { UserId = userId, ChatGroupId = id }));

        await _db.SaveChangesAsync(ct);
        return Ok(await LoadAsync(ct));
    }

    /// <summary>
    /// Sends a test message. Shows whether the bot can write to the user at all:
    /// Telegram forbids bots to message people who never pressed /start (or blocked the bot).
    /// </summary>
    [HttpPost("test")]
    public async Task<IActionResult> SendTest(CancellationToken ct)
    {
        try
        {
            await _bot.SendMessage(
                chatId: CurrentUser.TelegramId,
                text: "🔔 Тестовое уведомление. Всё работает — сюда будут приходить новые задания.",
                replyMarkup: MiniAppLink.Button(_config, "📱 Открыть приложение"),
                cancellationToken: ct);
            return Ok(new { ok = true });
        }
        catch (ApiRequestException ex) when (ex.ErrorCode is 400 or 403)
        {
            // "chat not found" / "bot was blocked by the user"
            return Conflict(new { error = "Бот не может вам написать. Откройте чат с ботом и нажмите «Старт» (или разблокируйте его)." });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Не удалось отправить тестовое уведомление {UserId}", CurrentUser.TelegramId);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Telegram сейчас недоступен. Попробуйте позже." });
        }
    }

    private async Task<NotificationSettingsDto> LoadAsync(CancellationToken ct)
    {
        var userId = CurrentUser.TelegramId;
        var s = await _db.NotificationSettings.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct)
                ?? new NotificationSettings { UserId = userId };
        var muted = await _db.MutedSubjects.Where(m => m.UserId == userId).Select(m => m.ChatGroupId).ToListAsync(ct);
        var subjects = await _db.ChatGroups.AsNoTracking().OrderBy(g => g.Id)
            .Select(g => new { g.Id, g.Name }).ToListAsync(ct);

        return new NotificationSettingsDto(
            s.NewTasks, s.AutoDelete, s.QuietHoursEnabled,
            NotificationPolicy.FormatMinutes(s.QuietFromMinutes),
            NotificationPolicy.FormatMinutes(s.QuietToMinutes),
            subjects.Select(g => new SubjectMuteDto(g.Id, g.Name, muted.Contains(g.Id))).ToList());
    }
}
