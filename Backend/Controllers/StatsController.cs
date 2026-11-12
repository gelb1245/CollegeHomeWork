using Microsoft.AspNetCore.Mvc;
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
    private readonly StatsService _stats;

    public StatsController(StatsService stats) => _stats = stats;

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
