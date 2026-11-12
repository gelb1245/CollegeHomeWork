using System.Globalization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TelegramBotApi.Attributes;
using TelegramBotApi.Data;
using TelegramBotApi.Models;
using TelegramBotApi.Services;

namespace TelegramBotApi.Controllers;

public record AttachmentDto(int Id, string FileName, string ContentType, long Size, bool IsImage, string Url);

public record MessageResponseDto(
    int Id,
    int ChatGroupId,
    long FromUserId,
    string Author,
    string Text,
    DateTime SentAt,
    DateTime? Deadline,
    DateTime? EditedAt,
    bool IsCompleted,
    DateTime? CompletedAt,
    IReadOnlyList<AttachmentDto> Attachments,
    // Only for Admin/Developer: how many students marked the task as done, out of how many
    int? CompletedCount = null,
    int? StudentsTotal = null);

/// <summary>Form for creating a homework message (multipart/form-data, files are optional).</summary>
public class HomeworkForm
{
    public string? Text { get; set; }

    /// <summary>ISO 8601 date (UTC); empty string = no deadline.</summary>
    public string? Deadline { get; set; }

    public List<IFormFile> Files { get; set; } = new();
}

/// <summary>Form for editing a homework message.</summary>
public class HomeworkEditForm : HomeworkForm
{
    public List<int> RemoveAttachmentIds { get; set; } = new();
}

/// <summary>
/// Homework messages: list, publish (with photos/PDF), edit, delete, completion marks and deadlines.
/// </summary>
[ApiController]
[Route("api/admin")]
[EnableRateLimiting("strict-api")]
[TelegramAuthorize]
public class HomeworkController : ControllerBase
{
    private const int MaxTextLength = 4000;

    private readonly TelegramBotContext _db;
    private readonly HomeworkNotificationService _notifications;
    private readonly AttachmentStorage _storage;
    private readonly AttachmentUrlSigner _signer;
    private readonly StatsService _stats;
    private readonly ILogger<HomeworkController> _log;

    private User CurrentUser => (User)HttpContext.Items["CurrentUser"]!;

    public HomeworkController(
        TelegramBotContext db,
        HomeworkNotificationService notifications,
        AttachmentStorage storage,
        AttachmentUrlSigner signer,
        StatsService stats,
        ILogger<HomeworkController> log)
    {
        _stats = stats;
        _db = db;
        _notifications = notifications;
        _storage = storage;
        _signer = signer;
        _log = log;
    }

    [HttpGet("group/{groupId}/messages")]
    public async Task<ActionResult<IEnumerable<MessageResponseDto>>> GetMessages(
        int groupId,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (groupId <= 0) return BadRequest("Некорректный ID группы.");
        limit = Math.Clamp(limit, 1, 100);

        var userId = CurrentUser.TelegramId;
        var isPrivileged = CurrentUser.Role is RoleType.Admin or RoleType.Developer;

        var rows = await _db.Messages
            .AsNoTracking()
            .Where(m => m.ChatGroupId == groupId)
            .OrderByDescending(m => m.SentAt)
            .Take(limit)
            .Select(m => new
            {
                m.Id,
                m.ChatGroupId,
                m.FromUserId,
                Author = m.FromUser != null ? (m.FromUser.FullName ?? m.FromUser.Username) : "Администратор",
                m.Text,
                m.SentAt,
                m.Deadline,
                m.EditedAt,
                Attachments = m.Attachments
                    .OrderBy(a => a.Id)
                    .Select(a => new { a.Id, a.FileName, a.ContentType, a.Size })
                    .ToList(),
                Progress = _db.UserTaskProgresses
                    .Where(p => p.MessageId == m.Id && p.UserId == userId)
                    .Select(p => new { p.IsCompleted, p.CompletedAt })
                    .FirstOrDefault(),
                CompletedCount = isPrivileged
                    ? _db.UserTaskProgresses.Count(p => p.MessageId == m.Id && p.IsCompleted &&
                        p.User != null && (p.User.Role == RoleType.User || p.User.Role == RoleType.Admin))
                    : 0
            })
            .ToListAsync(cancellationToken);

        int? studentsTotal = isPrivileged ? await _stats.CountStudentsAsync(cancellationToken) : null;

        var result = rows
            .OrderBy(m => m.SentAt)
            .Select(m => new MessageResponseDto(
                m.Id, m.ChatGroupId, m.FromUserId, m.Author, m.Text, m.SentAt, m.Deadline, m.EditedAt,
                m.Progress?.IsCompleted ?? false,
                m.Progress?.CompletedAt,
                m.Attachments.Select(a => ToDto(a.Id, a.FileName, a.ContentType, a.Size)).ToList(),
                isPrivileged ? m.CompletedCount : null,
                studentsTotal))
            .ToList();

        return Ok(result);
    }

    [HttpPost("group/{groupId}/message")]
    [TelegramAuthorize(RoleType.Admin, RoleType.Developer)]
    [RequestTimeout(120_000)] // files are uploaded to Telegram during the request
    public async Task<IActionResult> PostMessage(int groupId, [FromForm] HomeworkForm form, CancellationToken cancellationToken)
    {
        if (groupId <= 0) return BadRequest(new { error = "Некорректный ID группы." });

        var (text, deadline, error) = ParseTextAndDeadline(form);
        if (error != null) return BadRequest(new { error });

        error = await ValidateFilesAsync(form.Files, existingCount: 0, cancellationToken);
        if (error != null) return BadRequest(new { error });

        var group = await _db.ChatGroups.FindAsync(new object[] { groupId }, cancellationToken);
        if (group == null) return NotFound(new { error = "Предмет не найден." });

        var msg = new Message
        {
            ChatGroupId = groupId,
            FromUserId = CurrentUser.TelegramId,
            Text = text!,
            SentAt = DateTime.UtcNow,
            Deadline = deadline
        };

        // Files are stored first: if Telegram rejects one, nothing is published
        var uploaded = await UploadFilesAsync(form.Files, group.Name, cancellationToken);
        if (uploaded.Error != null) return StatusCode(StatusCodes.Status502BadGateway, new { error = uploaded.Error });
        msg.Attachments.AddRange(uploaded.Attachments);

        _db.Messages.Add(msg);
        await _db.SaveChangesAsync(cancellationToken);

        // Notify students in the background (notifications self-destruct after 24 h)
        var authorId = CurrentUser.TelegramId;
        var attachmentCount = msg.Attachments.Count;
        _ = Task.Run(async () =>
        {
            try
            {
                await _notifications.NotifyStudentsAsync(msg.Id, group.Id, group.Name, msg.Text, msg.Deadline, authorId, attachmentCount);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Ошибка при рассылке уведомлений о ДЗ #{Id}", msg.Id);
            }
        });

        return Ok(new { msg.Id });
    }

    [HttpPatch("message/{id}")]
    [TelegramAuthorize(RoleType.Admin, RoleType.Developer)]
    [RequestTimeout(120_000)]
    public async Task<IActionResult> EditMessage(int id, [FromForm] HomeworkEditForm form, CancellationToken cancellationToken)
    {
        if (id <= 0) return BadRequest(new { error = "Некорректный ID задания." });

        var msg = await _db.Messages
            .Include(m => m.Attachments)
            .Include(m => m.Group)
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (msg == null) return NotFound(new { error = "Задание не найдено." });

        var (text, deadline, error) = ParseTextAndDeadline(form);
        if (error != null) return BadRequest(new { error });

        var toRemove = msg.Attachments.Where(a => form.RemoveAttachmentIds.Contains(a.Id)).ToList();
        var remaining = msg.Attachments.Count - toRemove.Count;

        error = await ValidateFilesAsync(form.Files, remaining, cancellationToken);
        if (error != null) return BadRequest(new { error });

        var uploaded = await UploadFilesAsync(form.Files, msg.Group?.Name ?? "", cancellationToken);
        if (uploaded.Error != null) return StatusCode(StatusCodes.Status502BadGateway, new { error = uploaded.Error });

        msg.Text = text!;
        msg.Deadline = deadline;
        msg.EditedAt = DateTime.UtcNow;
        _db.Attachments.RemoveRange(toRemove);
        msg.Attachments.AddRange(uploaded.Attachments);

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { msg.Id });
    }

    [HttpDelete("message/{id}")]
    [TelegramAuthorize(RoleType.Admin, RoleType.Developer)]
    public async Task<IActionResult> DeleteMessage(int id, CancellationToken cancellationToken)
    {
        if (id <= 0) return BadRequest("Некорректный ID сообщения.");

        var msg = await _db.Messages.FindAsync(new object[] { id }, cancellationToken);
        if (msg == null) return NotFound();

        _db.Messages.Remove(msg); // attachments are removed by cascade
        await _db.SaveChangesAsync(cancellationToken);

        // Immediately delete already sent notifications from Telegram chats
        await _notifications.DeleteNotificationsForHomeworkAsync(id, cancellationToken);

        return NoContent();
    }

    [HttpPost("message/{id}/toggle-complete")]
    public async Task<IActionResult> ToggleComplete(int id, CancellationToken cancellationToken)
    {
        if (id <= 0) return BadRequest("Некорректный ID сообщения.");

        var messageExists = await _db.Messages.AnyAsync(m => m.Id == id, cancellationToken);
        if (!messageExists) return NotFound("Задание не найдено.");

        var userId = CurrentUser.TelegramId;
        var progress = await _db.UserTaskProgresses
            .FirstOrDefaultAsync(p => p.UserId == userId && p.MessageId == id, cancellationToken);

        if (progress == null)
        {
            progress = new UserTaskProgress { UserId = userId, MessageId = id, IsCompleted = true, CompletedAt = DateTime.UtcNow };
            _db.UserTaskProgresses.Add(progress);
        }
        else
        {
            progress.IsCompleted = !progress.IsCompleted;
            progress.CompletedAt = progress.IsCompleted ? DateTime.UtcNow : null;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { messageId = id, isCompleted = progress.IsCompleted, completedAt = progress.CompletedAt });
    }

    [HttpGet("my-deadlines")]
    public async Task<IActionResult> GetMyDeadlines(CancellationToken cancellationToken)
    {
        var userId = CurrentUser.TelegramId;

        var result = await _db.Messages
            .AsNoTracking()
            .Where(m => m.Deadline != null)
            .OrderBy(m => m.Deadline)
            .Select(m => new
            {
                m.Id,
                m.ChatGroupId,
                GroupName = m.Group != null ? m.Group.Name : "Общее",
                m.Text,
                m.SentAt,
                m.Deadline,
                IsCompleted = _db.UserTaskProgresses.Any(p => p.MessageId == m.Id && p.UserId == userId && p.IsCompleted),
                CompletedAt = _db.UserTaskProgresses
                    .Where(p => p.MessageId == m.Id && p.UserId == userId)
                    .Select(p => p.CompletedAt)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return Ok(result);
    }

    // ---------- helpers ----------

    private AttachmentDto ToDto(int id, string fileName, string contentType, long size) =>
        new(id, fileName, contentType, size, AttachmentStorage.IsImage(contentType), _signer.CreateUrl(id));

    private static (string? Text, DateTime? Deadline, string? Error) ParseTextAndDeadline(HomeworkForm form)
    {
        var text = form.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return (null, null, "Текст задания пуст.");
        if (text.Length > MaxTextLength) return (null, null, $"Текст слишком длинный (максимум {MaxTextLength} символов).");

        if (string.IsNullOrWhiteSpace(form.Deadline)) return (text, null, null);

        // Parse explicitly as UTC: default form binding would convert to the server's local time
        if (!DateTimeOffset.TryParse(form.Deadline, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return (null, null, "Некорректная дата дедлайна.");
        }

        return (text, parsed.UtcDateTime, null);
    }

    private static async Task<string?> ValidateFilesAsync(List<IFormFile> files, int existingCount, CancellationToken ct)
    {
        if (existingCount + files.Count > AttachmentStorage.MaxFilesPerMessage)
            return $"Можно прикрепить не больше {AttachmentStorage.MaxFilesPerMessage} файлов к одному заданию.";

        foreach (var file in files)
        {
            var error = await AttachmentStorage.ValidateAsync(file, ct);
            if (error != null) return error;
        }
        return null;
    }

    private async Task<(List<Attachment> Attachments, string? Error)> UploadFilesAsync(
        List<IFormFile> files, string subjectName, CancellationToken ct)
    {
        var result = new List<Attachment>();
        foreach (var file in files)
        {
            try
            {
                result.Add(await _storage.UploadAsync(file, CurrentUser.TelegramId, $"📎 Файл к заданию · {subjectName}", ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "Не удалось загрузить файл {FileName} в Telegram", file.FileName);
                return (result, "Не удалось сохранить файл в Telegram. Убедитесь, что вы запустили бота (/start), и попробуйте ещё раз.");
            }
        }
        return (result, null);
    }
}
