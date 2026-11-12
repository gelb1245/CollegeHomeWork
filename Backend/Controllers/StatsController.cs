using Microsoft.AspNetCore.Mvc;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Microsoft.AspNetCore.RateLimiting;
using TelegramBotApi.Attributes;
using TelegramBotApi.Models;
using TelegramBotApi.Services;

namespace TelegramBotApi.Controllers;

/// <summary>
/// Homework completion statistics. Only for the class monitor (Admin) and developers.
/// </summary>
[ApiController]
[Route("api/admin")]
[EnableRateLimiting("strict-api")]
[TelegramAuthorize(RoleType.Admin, RoleType.Developer)]
public class StatsController : ControllerBase
{
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly StatsService _stats;
    private readonly StatsExportService _export;
    private readonly ITelegramBotClient _bot;
    private readonly ILogger<StatsController> _log;

    private Models.User CurrentUser => (Models.User)HttpContext.Items["CurrentUser"]!;

    public StatsController(StatsService stats, StatsExportService export, ITelegramBotClient bot, ILogger<StatsController> log)
    {
        _stats = stats;
        _export = export;
        _bot = bot;
        _log = log;
    }

    /// <summary>Excel report as a file download (for a regular browser).</summary>
    [HttpGet("stats/export")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return File(await _export.BuildAsync(now, ct), XlsxType, StatsExportService.FileName(now));
    }

    /// <summary>
    /// Excel report sent by the bot into the user's Telegram chat — downloading files inside the
    /// Telegram Mini App is unreliable (especially on iOS), and from the chat it is easy to forward.
    /// </summary>
    [HttpPost("stats/export/send")]
    public async Task<IActionResult> SendExport(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var bytes = await _export.BuildAsync(now, ct);
        try
        {
            await using var stream = new MemoryStream(bytes);
            await _bot.SendDocument(
                chatId: CurrentUser.TelegramId,
                document: InputFile.FromStream(stream, StatsExportService.FileName(now)),
                caption: $"📊 Статистика сдачи заданий на {LocalTime.FromUtc(now):dd.MM.yyyy HH:mm}",
                cancellationToken: ct);
            return Ok(new { ok = true });
        }
        catch (ApiRequestException ex) when (ex.ErrorCode is 400 or 403)
        {
            return Conflict(new { error = "Бот не может вам написать. Откройте чат с ботом и нажмите «Старт»." });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Не удалось отправить выгрузку статистики");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Telegram сейчас недоступен. Попробуйте позже." });
        }
    }

    /// <summary>Totals, per-task and per-student completion.</summary>
    [HttpGet("stats")]
    public async Task<ActionResult<StatsOverview>> GetOverview(CancellationToken ct) =>
        Ok(await _stats.GetOverviewAsync(DateTime.UtcNow, ct));

    /// <summary>Who has and has not completed a task.</summary>
    [HttpGet("message/{id:int}/progress")]
    public async Task<ActionResult<TaskProgressDetails>> GetTaskProgress(int id, CancellationToken ct)
    {
        var details = await _stats.GetTaskProgressAsync(id, ct);
        return details == null ? NotFound(new { error = "Задание не найдено." }) : Ok(details);
    }

    /// <summary>Tasks a student has not completed yet.</summary>
    [HttpGet("stats/student/{telegramId:long}")]
    public async Task<ActionResult<StudentDetails>> GetStudent(long telegramId, CancellationToken ct)
    {
        var details = await _stats.GetStudentAsync(telegramId, DateTime.UtcNow, ct);
        return details == null ? NotFound(new { error = "Студент не найден." }) : Ok(details);
    }
}
