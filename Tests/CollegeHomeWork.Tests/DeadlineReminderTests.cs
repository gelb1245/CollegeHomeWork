using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TelegramBotApi.Data;
using TelegramBotApi.Models;
using TelegramBotApi.Services;
using Xunit;

namespace CollegeHomeWork.Tests;

public class DeadlineReminderTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
    private const long Dev = 1, Monitor = 2, Anna = 11, Boris = 12, Vera = 13;
    private static readonly long[] Whitelist = { Dev, Monitor, Anna, Boris, Vera };

    private readonly SqliteConnection _connection;
    private readonly TelegramBotContext _db;
    private readonly int _groupId;

    public DeadlineReminderTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new TelegramBotContext(new DbContextOptionsBuilder<TelegramBotContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _db.Users.AddRange(
            new User { TelegramId = Dev, Username = "dev", Role = RoleType.Developer },
            new User { TelegramId = Monitor, Username = "monitor", Role = RoleType.Admin },
            new User { TelegramId = Anna, Username = "anna" },
            new User { TelegramId = Boris, Username = "boris" },
            new User { TelegramId = Vera, Username = "vera" });
        var group = new ChatGroup { Name = "История" };
        _db.ChatGroups.Add(group);
        _db.SaveChanges();
        _groupId = group.Id;
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private int AddTask(DateTime? deadline, DateTime? sentAt = null, TaskKind kind = TaskKind.Homework)
    {
        var m = new Message { ChatGroupId = _groupId, FromUserId = Monitor, Text = "Реферат", Kind = kind,
            SentAt = sentAt ?? Now.AddDays(-3), Deadline = deadline };
        _db.Messages.Add(m);
        _db.SaveChanges();
        return m.Id;
    }

    private Task<List<PlannedReminder>> Plan(DateTime? now = null) =>
        new DeadlineReminderPlanner(_db).PlanAsync(Whitelist, now ?? Now, default);

    // ---------- stage choice ----------

    [Theory]
    [InlineData(30, null)]                           // more than a day left
    [InlineData(20, ReminderStage.DayBefore)]
    [InlineData(1.5, ReminderStage.HoursBefore)]
    [InlineData(-1, null)]                           // already overdue
    public void DueStage_ByTimeLeft(double hoursLeft, ReminderStage? expected)
    {
        Assert.Equal(expected, DeadlineReminderPlanner.DueStage(Now.AddDays(-5), Now.AddHours(hoursLeft), Now));
    }

    [Fact]
    public void DueStage_SkipsDayReminder_ForTaskPublishedInsideTheDay()
    {
        var deadline = Now.AddHours(20);
        Assert.Null(DeadlineReminderPlanner.DueStage(Now.AddHours(-1), deadline, Now)); // published 21h before deadline
        Assert.Equal(ReminderStage.HoursBefore, DeadlineReminderPlanner.DueStage(Now.AddHours(-1), deadline, deadline.AddHours(-1)));
    }

    // ---------- who gets it ----------

    [Fact]
    public async Task OnlyStudentsWhoHaveNotHandedIn_NotAuthorNotDeveloper()
    {
        var id = AddTask(Now.AddHours(10));
        _db.UserTaskProgresses.Add(new UserTaskProgress { UserId = Anna, MessageId = id, IsCompleted = true, CompletedAt = Now });
        _db.SaveChanges();

        var plan = await Plan();

        Assert.Equal(new[] { Boris, Vera }, plan.Select(p => p.UserId).OrderBy(x => x));
        Assert.All(plan, p => Assert.Equal(ReminderStage.DayBefore, p.Stage));
    }

    [Fact]
    public async Task LecturesAndTasksWithoutDeadline_HaveNoReminders()
    {
        AddTask(null);
        AddTask(Now.AddHours(5), kind: TaskKind.Lecture);
        Assert.Empty(await Plan());
    }

    [Fact]
    public async Task SentReminder_IsNotRepeated_ButNextStageComes()
    {
        var id = AddTask(Now.AddHours(10));
        _db.DeadlineReminders.AddRange(Whitelist.Select(u => new DeadlineReminder { MessageId = id, UserId = u, Stage = ReminderStage.DayBefore }));
        _db.SaveChanges();

        Assert.Empty(await Plan());
        var later = await Plan(Now.AddHours(9));
        Assert.Equal(new[] { Anna, Boris, Vera }, later.Where(p => p.Stage == ReminderStage.HoursBefore).Select(p => p.UserId).OrderBy(x => x));
    }

    [Fact]
    public async Task Settings_ReminderOff_MutedSubject_QuietHours()
    {
        // Quiet hours from 0:00 to 23:59 — "now" is inside them in any configured time zone
        AddTask(Now.AddHours(10));
        _db.NotificationSettings.AddRange(
            new NotificationSettings { UserId = Anna, DeadlineReminders = false },
            new NotificationSettings { UserId = Vera, QuietHoursEnabled = true, QuietFromMinutes = 0, QuietToMinutes = 23 * 60 + 59, AutoDelete = false });
        _db.MutedSubjects.Add(new MutedSubject { UserId = Boris, ChatGroupId = _groupId });
        _db.SaveChanges();

        var vera = Assert.Single(await Plan());
        Assert.Equal(Vera, vera.UserId);
        Assert.True(vera.Silent);
        Assert.False(vera.AutoDelete);
    }

    [Fact]
    public void Text_HasSubjectDeadlineAndNoMarkdownFromTask()
    {
        var text = DeadlineReminderPlanner.BuildText(new PlannedReminder(1, Anna, ReminderStage.HoursBefore, "История",
            "**Реферат** про_Петра", Now, false, true));
        Assert.Contains("меньше 2 часов", text);
        Assert.Contains("История", text);
        Assert.DoesNotContain("**Реферат**", text);
    }
}
