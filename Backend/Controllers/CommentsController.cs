using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using TelegramBotApi.Attributes;
using TelegramBotApi.Data;
using TelegramBotApi.Models;
using TelegramBotApi.Services;
using TelegramBotApi.Services.Bot;

namespace TelegramBotApi.Controllers;

public record CommentDto(int Id, long UserId, string Author, bool IsStaff, string Text, DateTime CreatedAt, bool CanDelete);

public record CreateCommentDto(string? Text);

/// <summary>
/// Questions under a task ("сколько страниц?"). Everyone in the class sees them and the answers.
/// A question notifies the task author; an answer from staff notifies everyone who asked.
/// </summary>
[ApiController]
[Route("api/admin")]
[EnableRateLimiting("strict-api")]
[TelegramAuthorize]
public class CommentsController : ControllerBase
{
    public const int MaxLength = 1000;

    private readonly TelegramBotContext _db;
    private readonly ITelegramBotClient _bot;
    private readonly IConfiguration _config;
    private readonly ILogger<CommentsController> _log;

    private User CurrentUser => (User)HttpContext.Items["CurrentUser"]!;
    private bool IsStaff => CurrentUser.Role is RoleType.Admin or RoleType.Developer;

    public CommentsController(TelegramBotContext db, ITelegramBotClient bot, IConfiguration config, ILogger<CommentsController> log)
    {
        _db = db;
        _bot = bot;
        _config = config;
        _log = log;
    }

    [HttpGet("message/{id}/comments")]
    public async Task<ActionResult<IEnumerable<CommentDto>>> List(int id, CancellationToken ct)
    {
        if (!await _db.Messages.AnyAsync(m => m.Id == id, ct)) return NotFound(new { error = "Задание не найдено." });

        var rows = await _db.TaskComments.AsNoTracking()
            .Where(c => c.MessageId == id)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new
            {
                c.Id, c.UserId, c.Text, c.CreatedAt,
                Author = c.User != null ? (c.User.FullName ?? c.User.Username) : "Пользователь",
                Role = c.User != null ? c.User.Role : RoleType.User
            })
            .ToListAsync(ct);

        var me = CurrentUser.TelegramId;
        return Ok(rows.Select(c => new CommentDto(c.Id, c.UserId, c.Author, c.Role != RoleType.User, c.Text, c.CreatedAt,
            c.UserId == me || IsStaff)));
    }

    [HttpPost("message/{id}/comments")]
    public async Task<IActionResult> Create(int id, [FromBody] CreateCommentDto dto, CancellationToken ct)
    {
        var text = dto.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return BadRequest(new { error = "Пустое сообщение." });
        if (text.Length > MaxLength) return BadRequest(new { error = $"Слишком длинно (максимум {MaxLength} символов)." });

        var task = await _db.Messages.AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new { m.Id, m.FromUserId, m.Text, Subject = m.Group != null ? m.Group.Name : "" })
            .FirstOrDefaultAsync(ct);
        if (task == null) return NotFound(new { error = "Задание не найдено." });

        var comment = new TaskComment { MessageId = id, UserId = CurrentUser.TelegramId, Text = text, CreatedAt = DateTime.UtcNow };
        _db.TaskComments.Add(comment);
        await _db.SaveChangesAsync(ct);

        // Who should know: the author learns about a question, the people who asked learn about an answer
        var recipients = IsStaff
            ? await _db.TaskComments.Where(c => c.MessageId == id && c.UserId != CurrentUser.TelegramId
                    && c.User != null && c.User.Role == RoleType.User)
                .Select(c => c.UserId).Distinct().ToListAsync(ct)
            : new List<long> { task.FromUserId };
        recipients.Remove(CurrentUser.TelegramId);

        var notice = BuildNotice(IsStaff, CurrentUser.FullName ?? CurrentUser.Username, task.Subject, task.Text, text);
        var button = MiniAppLink.Button(_config, "💬 Открыть обсуждение");
        _ = Task.Run(async () =>
        {
            foreach (var userId in recipients)
            {
                try
                {
                    await _bot.SendMessage(userId, notice, parseMode: ParseMode.Markdown, replyMarkup: button);
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "Не удалось уведомить {UserId} о комментарии", userId);
                }
            }
        });

        return Ok(new CommentDto(comment.Id, comment.UserId, CurrentUser.FullName ?? CurrentUser.Username, IsStaff,
            comment.Text, comment.CreatedAt, true));
    }

    [HttpDelete("comments/{commentId}")]
    public async Task<IActionResult> Delete(int commentId, CancellationToken ct)
    {
        var comment = await _db.TaskComments.FindAsync(new object[] { commentId }, ct);
        if (comment == null) return NotFound();
        if (comment.UserId != CurrentUser.TelegramId && !IsStaff)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Можно удалить только своё сообщение." });

        _db.TaskComments.Remove(comment);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    public static string BuildNotice(bool isAnswer, string author, string subject, string taskText, string comment)
    {
        var task = SearchService.PlainText(taskText);
        if (task.Length > 80) task = task[..80] + "…";
        var head = isAnswer ? "💬 *Ответ к заданию*" : "❓ *Вопрос к заданию*";
        return $"{head} · {BotText.EscapeMarkdown(subject)}\n_{BotText.EscapeMarkdown(task)}_\n\n" +
               $"*{BotText.EscapeMarkdown(author)}:* {BotText.EscapeMarkdown(comment)}";
    }
}
