using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TelegramBotApi.Attributes;
using TelegramBotApi.Data;
using TelegramBotApi.Models;
using TelegramBotApi.Services;

namespace TelegramBotApi.Controllers;

public record LessonDto(int Id, int GroupId, string GroupName, int DayOfWeek, string Start, string End, string? Room);

public record CreateLessonDto(int GroupId, int DayOfWeek, string? Start, string? End, string? Room);

public record TodayTaskDto(int Id, string Text, DateTime? Deadline);

public record TodayLessonDto(LessonDto Lesson, int PendingCount, IReadOnlyList<TodayTaskDto> Pending);

public record TodayDto(string Date, int DayOfWeek, bool IsTomorrow, IReadOnlyList<TodayLessonDto> Lessons);

/// <summary>Weekly timetable. Everyone sees it; monitors and developers edit it.</summary>
[ApiController]
[Route("api/admin/schedule")]
[EnableRateLimiting("strict-api")]
[TelegramAuthorize]
public class ScheduleController : ControllerBase
{
    private readonly TelegramBotContext _db;

    private User CurrentUser => (User)HttpContext.Items["CurrentUser"]!;

    public ScheduleController(TelegramBotContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<LessonDto>>> GetWeek(CancellationToken ct) =>
        Ok(await LoadLessonsAsync(null, ct));

    /// <summary>
    /// Today's classes and what is still to do for each subject.
    /// After the last class of the day it shows tomorrow — that is what students prepare for.
    /// </summary>
    [HttpGet("today")]
    public async Task<ActionResult<TodayDto>> GetToday(CancellationToken ct)
    {
        var nowLocal = LocalTime.FromUtc(DateTime.UtcNow);
        var week = await LoadLessonsAsync(null, ct);
        var (date, lessons, isTomorrow) = PickDay(week, nowLocal);

        var groupIds = lessons.Select(l => l.GroupId).Distinct().ToList();
        var userId = CurrentUser.TelegramId;
        var pending = await _db.Messages.AsNoTracking()
            .Where(m => groupIds.Contains(m.ChatGroupId) && m.Kind == TaskKind.Homework
                && !_db.UserTaskProgresses.Any(p => p.MessageId == m.Id && p.UserId == userId && p.IsCompleted))
            .Select(m => new { m.Id, m.ChatGroupId, m.Text, m.Deadline, m.SentAt })
            .ToListAsync(ct);
        // Old tasks without a deadline do not hang in "today" forever
        var cutoff = DateTime.UtcNow.AddDays(-14);
        var relevant = pending.Where(m => m.Deadline != null ? m.Deadline > cutoff : m.SentAt > cutoff).ToList();

        var result = lessons.Select(l =>
        {
            var tasks = relevant.Where(m => m.ChatGroupId == l.GroupId)
                .OrderBy(m => m.Deadline ?? DateTime.MaxValue).ThenByDescending(m => m.SentAt).ToList();
            return new TodayLessonDto(l, tasks.Count,
                tasks.Take(3).Select(m => new TodayTaskDto(m.Id, Snippet(m.Text), m.Deadline)).ToList());
        }).ToList();

        return Ok(new TodayDto(date.ToString("yyyy-MM-dd"), IsoDay(date.DayOfWeek), isTomorrow, result));
    }

    [HttpPost]
    [TelegramAuthorize(RoleType.Admin, RoleType.Developer)]
    public async Task<IActionResult> Create([FromBody] CreateLessonDto dto, CancellationToken ct)
    {
        if (dto.DayOfWeek is < 1 or > 7) return BadRequest(new { error = "Некорректный день недели." });
        if (!NotificationPolicy.TryParseTime(dto.Start, out var start) || !NotificationPolicy.TryParseTime(dto.End, out var end))
            return BadRequest(new { error = "Время должно быть в формате ЧЧ:ММ." });
        if (end <= start) return BadRequest(new { error = "Пара должна заканчиваться позже, чем начинается." });
        if (!await _db.ChatGroups.AnyAsync(g => g.Id == dto.GroupId, ct)) return NotFound(new { error = "Предмет не найден." });

        var room = string.IsNullOrWhiteSpace(dto.Room) ? null : dto.Room.Trim();
        if (room?.Length > 50) return BadRequest(new { error = "Слишком длинное название аудитории." });

        var lesson = new Lesson { ChatGroupId = dto.GroupId, DayOfWeek = dto.DayOfWeek, StartMinutes = start, EndMinutes = end, Room = room };
        _db.Lessons.Add(lesson);
        await _db.SaveChangesAsync(ct);
        return Ok((await LoadLessonsAsync(lesson.Id, ct)).Single());
    }

    [HttpDelete("{id}")]
    [TelegramAuthorize(RoleType.Admin, RoleType.Developer)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var deleted = await _db.Lessons.Where(l => l.Id == id).ExecuteDeleteAsync(ct);
        return deleted > 0 ? NoContent() : NotFound();
    }

    // ---------- helpers ----------

    public static int IsoDay(DayOfWeek day) => day == System.DayOfWeek.Sunday ? 7 : (int)day;

    /// <summary>
    /// Today, or tomorrow once today's classes are over (or there are none today).
    /// Skips days without classes, up to a week ahead.
    /// </summary>
    public static (DateOnly Date, List<LessonDto> Lessons, bool IsTomorrow) PickDay(IReadOnlyList<LessonDto> week, DateTime nowLocal)
    {
        var today = DateOnly.FromDateTime(nowLocal);
        var minutes = nowLocal.Hour * 60 + nowLocal.Minute;
        var todays = week.Where(l => l.DayOfWeek == IsoDay(nowLocal.DayOfWeek)).ToList();
        if (todays.Any(l => ToMinutes(l.End) > minutes)) return (today, todays, false);

        for (var i = 1; i <= 7; i++)
        {
            var day = today.AddDays(i);
            var lessons = week.Where(l => l.DayOfWeek == IsoDay(day.DayOfWeek)).ToList();
            if (lessons.Count > 0) return (day, lessons, true);
        }
        return (today, new List<LessonDto>(), false);
    }

    private static int ToMinutes(string hhmm) => NotificationPolicy.TryParseTime(hhmm, out var m) ? m : 0;

    private static string Snippet(string text)
    {
        var plain = SearchService.PlainText(text).Replace('\n', ' ');
        return plain.Length > 90 ? plain[..90] + "…" : plain;
    }

    private async Task<List<LessonDto>> LoadLessonsAsync(int? id, CancellationToken ct)
    {
        var query = _db.Lessons.AsNoTracking();
        if (id != null) query = query.Where(l => l.Id == id);
        var rows = await query
            .OrderBy(l => l.DayOfWeek).ThenBy(l => l.StartMinutes)
            .Select(l => new { l.Id, l.ChatGroupId, Name = l.Group != null ? l.Group.Name : "", l.DayOfWeek, l.StartMinutes, l.EndMinutes, l.Room })
            .ToListAsync(ct);
        return rows.Select(l => new LessonDto(l.Id, l.ChatGroupId, l.Name, l.DayOfWeek,
            NotificationPolicy.FormatMinutes(l.StartMinutes), NotificationPolicy.FormatMinutes(l.EndMinutes), l.Room)).ToList();
    }
}
