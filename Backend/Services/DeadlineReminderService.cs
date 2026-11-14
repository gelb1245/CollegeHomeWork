using System.Text;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using TelegramBotApi.Data;
using TelegramBotApi.Models;

namespace TelegramBotApi.Services;

/// <summary>One reminder to send: who, about which task, how.</summary>
public record PlannedReminder(
    int MessageId,
    long UserId,
    ReminderStage Stage,
    string SubjectName,
    string Text,
    DateTime Deadline,
    bool Silent,
    bool AutoDelete);

/// <summary>
/// Decides who gets a deadline reminder. Pure database logic without Telegram, so it is tested directly.
/// Rules: homework only, a day and two hours before the deadline, only students who have not marked
/// the task as done, never the author, respecting reminder settings, muted subjects and quiet hours.
/// </summary>
public class DeadlineReminderPlanner
{
    public static readonly TimeSpan DayWindow = TimeSpan.FromHours(24);
    public static readonly TimeSpan HoursWindow = TimeSpan.FromHours(2);

    private readonly TelegramBotContext _db;

    public DeadlineReminderPlanner(TelegramBotContext db) => _db = db;

    /// <summary>
    /// Which reminder is due now, if any. A stage is skipped when the task was published inside its
    /// window: the "new task" message already said the deadline is close.
    /// </summary>
    public static ReminderStage? DueStage(DateTime sentAt, DateTime deadline, DateTime now)
    {
        var left = deadline - now;
        if (left <= TimeSpan.Zero) return null;
        if (left <= HoursWindow) return sentAt <= deadline - HoursWindow ? ReminderStage.HoursBefore : null;
        if (left <= DayWindow) return sentAt <= deadline - DayWindow ? ReminderStage.DayBefore : null;
        return null;
    }

    public async Task<List<PlannedReminder>> PlanAsync(IReadOnlyCollection<long> whitelist, DateTime now, CancellationToken ct)
    {
        var until = now + DayWindow;
        var tasks = await _db.Messages.AsNoTracking()
            .Where(m => m.Kind == TaskKind.Homework && m.Deadline != null && m.Deadline > now && m.Deadline <= until)
            .Select(m => new { m.Id, m.ChatGroupId, m.FromUserId, m.Text, m.SentAt, Deadline = m.Deadline!.Value,
                SubjectName = m.Group != null ? m.Group.Name : "" })
            .ToListAsync(ct);

        var due = tasks
            .Select(t => (Task: t, Stage: DueStage(t.SentAt, t.Deadline, now)))
            .Where(x => x.Stage != null)
            .ToList();
        if (due.Count == 0) return new List<PlannedReminder>();

        var ids = due.Select(x => x.Task.Id).ToList();
        var done = (await _db.UserTaskProgresses.AsNoTracking()
                .Where(p => ids.Contains(p.MessageId) && p.IsCompleted)
                .Select(p => new { p.MessageId, p.UserId })
                .ToListAsync(ct))
            .Select(p => (p.MessageId, p.UserId)).ToHashSet();
        var alreadySent = (await _db.DeadlineReminders.AsNoTracking()
                .Where(r => ids.Contains(r.MessageId))
                .Select(r => new { r.MessageId, r.UserId, r.Stage })
                .ToListAsync(ct))
            .Select(r => (r.MessageId, r.UserId, r.Stage)).ToHashSet();

        var users = whitelist.ToList();
        // Developers are not students: they have nothing to hand in
        var developers = (await _db.Users.AsNoTracking()
                .Where(u => u.Role == RoleType.Developer && users.Contains(u.TelegramId))
                .Select(u => u.TelegramId).ToListAsync(ct))
            .ToHashSet();
        var settings = await _db.NotificationSettings.AsNoTracking()
            .Where(s => users.Contains(s.UserId))
            .ToDictionaryAsync(s => s.UserId, ct);
        var muted = (await _db.MutedSubjects.AsNoTracking()
                .Where(m => users.Contains(m.UserId))
                .Select(m => new { m.UserId, m.ChatGroupId })
                .ToListAsync(ct))
            .Select(m => (m.UserId, m.ChatGroupId)).ToHashSet();
        var localTime = TimeOnly.FromDateTime(LocalTime.FromUtc(now));

        var plan = new List<PlannedReminder>();
        foreach (var (t, stage) in due)
        {
            foreach (var userId in users)
            {
                if (userId == t.FromUserId || developers.Contains(userId)) continue;
                if (done.Contains((t.Id, userId))) continue;
                if (alreadySent.Contains((t.Id, userId, stage!.Value))) continue;
                settings.TryGetValue(userId, out var s);
                if (!(s?.DeadlineReminders ?? true) || muted.Contains((userId, t.ChatGroupId))) continue;

                plan.Add(new PlannedReminder(t.Id, userId, stage.Value, t.SubjectName, t.Text, t.Deadline,
                    NotificationPolicy.IsQuietTime(s, localTime), NotificationPolicy.ShouldAutoDelete(s)));
            }
        }
        return plan;
    }

    public static string BuildText(PlannedReminder r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.Stage == ReminderStage.HoursBefore
            ? "⏰ *До дедлайна меньше 2 часов!*"
            : "⏳ *Завтра дедлайн*");
        sb.AppendLine($"📚 Предмет: *{Bot.BotText.EscapeMarkdown(r.SubjectName)}*");
        sb.AppendLine($"🕐 Срок: `{LocalTime.FromUtc(r.Deadline):dd.MM HH:mm}`\n");
        var preview = SearchService.PlainText(r.Text);
        if (preview.Length > 300) preview = preview[..300] + "…";
        sb.AppendLine(Bot.BotText.EscapeMarkdown(preview));
        sb.AppendLine();
        sb.Append("_Уже сдали? Отметьте «Сдано» в Mini App — и напоминаний больше не будет._");
        return sb.ToString();
    }
}

/// <summary>Every 5 minutes sends the reminders planned by <see cref="DeadlineReminderPlanner"/>.</summary>
public class DeadlineReminderService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITelegramBotClient _bot;
    private readonly WhitelistService _whitelist;
    private readonly IConfiguration _config;
    private readonly ILogger<DeadlineReminderService> _logger;

    public DeadlineReminderService(IServiceScopeFactory scopeFactory, ITelegramBotClient bot, WhitelistService whitelist,
        IConfiguration config, ILogger<DeadlineReminderService> logger)
    {
        _scopeFactory = scopeFactory;
        _bot = bot;
        _whitelist = whitelist;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the app finish starting (migrations, bot) before the first check
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Ошибка при отправке напоминаний о дедлайнах");
            }

            try { await Task.Delay(CheckInterval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    public async Task RunOnceAsync(DateTime now, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TelegramBotContext>();
        var plan = await new DeadlineReminderPlanner(db).PlanAsync(_whitelist.GetAll(), now, ct);
        if (plan.Count == 0) return;

        var button = Bot.MiniAppLink.Button(_config, "📱 Открыть в Mini App");
        var sent = 0;
        foreach (var r in plan)
        {
            // Remembered even if sending fails (bot blocked): otherwise we would retry every 5 minutes
            db.DeadlineReminders.Add(new DeadlineReminder { MessageId = r.MessageId, UserId = r.UserId, Stage = r.Stage, SentAt = now });
            try
            {
                var msg = await _bot.SendMessage(r.UserId, DeadlineReminderPlanner.BuildText(r),
                    parseMode: ParseMode.Markdown, replyMarkup: button, disableNotification: r.Silent, cancellationToken: ct);
                // Deleted together with the task; auto-deleted after 24 h if the user wants that
                db.SentNotifications.Add(new SentNotification
                {
                    TelegramChatId = r.UserId,
                    TelegramMessageId = msg.Id,
                    HomeworkMessageId = r.MessageId,
                    SentAt = now,
                    DeleteAt = r.AutoDelete ? now.Add(HomeworkNotificationService.SelfDestructDelay) : HomeworkNotificationService.KeepForever
                });
                sent++;
                await Task.Delay(50, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Не удалось отправить напоминание пользователю {UserId}", r.UserId);
            }
        }
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Напоминания о дедлайнах: отправлено {Sent} из {Total}", sent, plan.Count);
    }
}
