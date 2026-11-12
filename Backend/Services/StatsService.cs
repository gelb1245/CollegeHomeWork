using Microsoft.EntityFrameworkCore;
using TelegramBotApi.Data;
using TelegramBotApi.Models;

namespace TelegramBotApi.Services;

public record StatsTotals(int Students, int Tasks, int CompletionPercent, int OverdueCount);

public record TaskStats(int Id, int GroupId, string GroupName, string Text, DateTime SentAt, DateTime? Deadline,
    int Completed, int Total);

public record StudentStats(long TelegramId, string Name, string? Username, RoleType Role,
    int Completed, int Total, int Overdue);

public record StatsOverview(StatsTotals Totals, IReadOnlyList<TaskStats> Tasks, IReadOnlyList<StudentStats> Students);

public record PersonRef(long TelegramId, string Name, DateTime? CompletedAt);

public record TaskProgressDetails(int Id, string GroupName, string Text, DateTime? Deadline,
    IReadOnlyList<PersonRef> Done, IReadOnlyList<PersonRef> NotDone);

public record PendingTask(int Id, int GroupId, string GroupName, string Text, DateTime? Deadline, bool IsOverdue);

public record StudentDetails(long TelegramId, string Name, int Completed, int Total, IReadOnlyList<PendingTask> Pending);

/// <summary>
/// Homework completion statistics for the class monitor (Admin) and developers.
/// "Students" are users with role User or Admin (the monitor does homework too);
/// developers are not counted. A task is overdue for a student when its deadline passed
/// and the student has not marked it as done.
/// </summary>
public class StatsService
{
    private static readonly RoleType[] StudentRoles = { RoleType.User, RoleType.Admin };
    private readonly TelegramBotContext _db;

    public StatsService(TelegramBotContext db) => _db = db;

    public async Task<StatsOverview> GetOverviewAsync(DateTime nowUtc, CancellationToken ct)
    {
        var students = await StudentsQuery().ToListAsync(ct);
        var studentIds = students.Select(s => s.TelegramId).ToHashSet();

        var tasks = await _db.Messages.AsNoTracking()
            .OrderByDescending(m => m.SentAt)
            .Select(m => new { m.Id, m.ChatGroupId, GroupName = m.Group != null ? m.Group.Name : "", m.Text, m.SentAt, m.Deadline })
            .ToListAsync(ct);

        // (task, student) pairs that are done
        var done = (await _db.UserTaskProgresses.AsNoTracking()
                .Where(p => p.IsCompleted)
                .Select(p => new { p.MessageId, p.UserId })
                .ToListAsync(ct))
            .Where(p => studentIds.Contains(p.UserId))
            .ToList();

        var doneByTask = done.GroupBy(p => p.MessageId).ToDictionary(g => g.Key, g => g.Count());
        var doneSet = done.Select(p => (p.MessageId, p.UserId)).ToHashSet();
        var overdueTaskIds = tasks.Where(t => t.Deadline < nowUtc).Select(t => t.Id).ToList();

        var taskStats = tasks
            .Select(t => new TaskStats(t.Id, t.ChatGroupId, t.GroupName, t.Text, t.SentAt, t.Deadline,
                doneByTask.GetValueOrDefault(t.Id), students.Count))
            .ToList();

        var studentStats = students
            .Select(s => new StudentStats(
                s.TelegramId, DisplayName(s), s.Username, s.Role,
                Completed: tasks.Count(t => doneSet.Contains((t.Id, s.TelegramId))),
                Total: tasks.Count,
                Overdue: overdueTaskIds.Count(id => !doneSet.Contains((id, s.TelegramId)))))
            // Who is behind comes first
            .OrderByDescending(s => s.Overdue)
            .ThenBy(s => s.Total == 0 ? 1.0 : (double)s.Completed / s.Total)
            .ThenBy(s => s.Name)
            .ToList();

        var pairs = students.Count * tasks.Count;
        var totals = new StatsTotals(
            Students: students.Count,
            Tasks: tasks.Count,
            CompletionPercent: pairs == 0 ? 0 : (int)Math.Round(100.0 * done.Count / pairs),
            OverdueCount: studentStats.Sum(s => s.Overdue));

        return new StatsOverview(totals, taskStats, studentStats);
    }

    public async Task<TaskProgressDetails?> GetTaskProgressAsync(int messageId, CancellationToken ct)
    {
        var task = await _db.Messages.AsNoTracking()
            .Where(m => m.Id == messageId)
            .Select(m => new { m.Id, GroupName = m.Group != null ? m.Group.Name : "", m.Text, m.Deadline })
            .FirstOrDefaultAsync(ct);
        if (task == null) return null;

        var students = await StudentsQuery().ToListAsync(ct);
        var completedAt = await _db.UserTaskProgresses.AsNoTracking()
            .Where(p => p.MessageId == messageId && p.IsCompleted)
            .ToDictionaryAsync(p => p.UserId, p => p.CompletedAt, ct);

        var doneList = students
            .Where(s => completedAt.ContainsKey(s.TelegramId))
            .Select(s => new PersonRef(s.TelegramId, DisplayName(s), completedAt[s.TelegramId]))
            .OrderBy(p => p.CompletedAt)
            .ToList();
        var notDone = students
            .Where(s => !completedAt.ContainsKey(s.TelegramId))
            .Select(s => new PersonRef(s.TelegramId, DisplayName(s), null))
            .OrderBy(p => p.Name)
            .ToList();

        return new TaskProgressDetails(task.Id, task.GroupName, task.Text, task.Deadline, doneList, notDone);
    }

    public async Task<StudentDetails?> GetStudentAsync(long telegramId, DateTime nowUtc, CancellationToken ct)
    {
        var student = await StudentsQuery().FirstOrDefaultAsync(u => u.TelegramId == telegramId, ct);
        if (student == null) return null;

        var doneIds = await _db.UserTaskProgresses.AsNoTracking()
            .Where(p => p.UserId == telegramId && p.IsCompleted)
            .Select(p => p.MessageId)
            .ToListAsync(ct);

        var tasks = await _db.Messages.AsNoTracking()
            .Select(m => new { m.Id, m.ChatGroupId, GroupName = m.Group != null ? m.Group.Name : "", m.Text, m.Deadline })
            .ToListAsync(ct);

        var pending = tasks
            .Where(t => !doneIds.Contains(t.Id))
            .Select(t => new PendingTask(t.Id, t.ChatGroupId, t.GroupName, t.Text, t.Deadline, t.Deadline < nowUtc))
            // Overdue first, then by nearest deadline, tasks without deadline last
            .OrderByDescending(t => t.IsOverdue)
            .ThenBy(t => t.Deadline ?? DateTime.MaxValue)
            .ToList();

        return new StudentDetails(student.TelegramId, DisplayName(student),
            tasks.Count(t => doneIds.Contains(t.Id)), tasks.Count, pending);
    }

    /// <summary>Number of students — the denominator for "сдали X из N".</summary>
    public Task<int> CountStudentsAsync(CancellationToken ct) => StudentsQuery().CountAsync(ct);

    private IQueryable<User> StudentsQuery() =>
        _db.Users.AsNoTracking().Where(u => StudentRoles.Contains(u.Role));

    private static string DisplayName(User u) =>
        !string.IsNullOrWhiteSpace(u.FullName) ? u.FullName! : u.Username;
}
