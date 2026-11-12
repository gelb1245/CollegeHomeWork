using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TelegramBotApi.Data;
using TelegramBotApi.Models;
using TelegramBotApi.Services;
using Xunit;

namespace CollegeHomeWork.Tests;

/// <summary>
/// Statistics on a real (in-memory SQLite) database.
/// Class: developer (not counted), monitor (Admin, counted), three students.
/// Tasks: T1 deadline passed, T2 deadline in future, T3 without deadline.
/// Done: s1 → T1, T2 · s2 → T2 · monitor → T1 · developer → T1 (ignored).
/// </summary>
public class StatsServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

    private const long Dev = 1, Monitor = 2, S1 = 11, S2 = 12, S3 = 13;
    private readonly SqliteConnection _connection;
    private readonly TelegramBotContext _db;
    private readonly int _t1, _t2, _t3;

    public StatsServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new TelegramBotContext(new DbContextOptionsBuilder<TelegramBotContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _db.Users.AddRange(
            new User { TelegramId = Dev, Username = "dev", Role = RoleType.Developer },
            new User { TelegramId = Monitor, Username = "monitor", FullName = "Староста", Role = RoleType.Admin },
            new User { TelegramId = S1, Username = "s1", FullName = "Анна", Role = RoleType.User },
            new User { TelegramId = S2, Username = "s2", FullName = "Борис", Role = RoleType.User },
            new User { TelegramId = S3, Username = "s3", FullName = "Вера", Role = RoleType.User });
        var group = new ChatGroup { Name = "История" };
        _db.ChatGroups.Add(group);
        _db.SaveChanges();

        Message Task(string text, DateTime? deadline) => new()
        {
            ChatGroupId = group.Id, FromUserId = Monitor, Text = text, SentAt = Now.AddDays(-5), Deadline = deadline
        };
        var t1 = Task("T1", Now.AddDays(-1));
        var t2 = Task("T2", Now.AddDays(2));
        var t3 = Task("T3", null);
        _db.Messages.AddRange(t1, t2, t3);
        _db.SaveChanges();
        (_t1, _t2, _t3) = (t1.Id, t2.Id, t3.Id);

        void Done(long user, int task) => _db.UserTaskProgresses.Add(
            new UserTaskProgress { UserId = user, MessageId = task, IsCompleted = true, CompletedAt = Now });
        Done(S1, _t1); Done(S1, _t2); Done(S2, _t2); Done(Monitor, _t1); Done(Dev, _t1);
        // A mark that was un-done must not count
        _db.UserTaskProgresses.Add(new UserTaskProgress { UserId = S3, MessageId = _t3, IsCompleted = false });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private StatsService Service => new(_db);

    [Fact]
    public async Task Totals_CountStudentsAndMonitor_NotDevelopers()
    {
        var totals = (await Service.GetOverviewAsync(Now, default)).Totals;

        Assert.Equal(4, totals.Students);          // monitor + 3 students
        Assert.Equal(3, totals.Tasks);
        Assert.Equal(33, totals.CompletionPercent); // 4 done of 4×3 = 12
        Assert.Equal(2, totals.OverdueCount);       // T1 not done by s2 and s3
    }

    [Fact]
    public async Task TaskStats_IgnoreDeveloperMarks()
    {
        var t1 = (await Service.GetOverviewAsync(Now, default)).Tasks.Single(t => t.Id == _t1);

        Assert.Equal(2, t1.Completed); // s1 + monitor, developer ignored
        Assert.Equal(4, t1.Total);
    }

    [Fact]
    public async Task Students_LaggingFirst()
    {
        var students = (await Service.GetOverviewAsync(Now, default)).Students;

        Assert.Equal(new[] { "Вера", "Борис", "Староста", "Анна" }, students.Select(s => s.Name));
        var vera = students[0];
        Assert.Equal((0, 3, 1), (vera.Completed, vera.Total, vera.Overdue));
        var anna = students[3];
        Assert.Equal((2, 3, 0), (anna.Completed, anna.Total, anna.Overdue));
    }

    [Fact]
    public async Task TaskProgress_ListsWhoDidAndDidNot()
    {
        var p = await Service.GetTaskProgressAsync(_t1, default);

        Assert.NotNull(p);
        Assert.Equal(new[] { S1, Monitor }.OrderBy(x => x), p!.Done.Select(x => x.TelegramId).OrderBy(x => x));
        Assert.Equal(new[] { "Борис", "Вера" }, p.NotDone.Select(x => x.Name));
    }

    [Fact]
    public async Task StudentDetails_OverdueFirst_ThenByDeadline_NoDeadlineLast()
    {
        var vera = await Service.GetStudentAsync(S3, Now, default);

        Assert.NotNull(vera);
        Assert.Equal(new[] { _t1, _t2, _t3 }, vera!.Pending.Select(t => t.Id));
        Assert.True(vera.Pending[0].IsOverdue);
        Assert.False(vera.Pending[1].IsOverdue);
    }

    [Fact]
    public async Task DeveloperIsNotAStudent()
    {
        Assert.Null(await Service.GetStudentAsync(Dev, Now, default));
        Assert.Equal(4, await Service.CountStudentsAsync(default));
    }

    [Fact]
    public async Task UnknownTask_ReturnsNull()
    {
        Assert.Null(await Service.GetTaskProgressAsync(99999, default));
    }
}
