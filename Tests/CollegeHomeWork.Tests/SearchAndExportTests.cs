using ClosedXML.Excel;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TelegramBotApi.Data;
using TelegramBotApi.Models;
using TelegramBotApi.Services;
using Xunit;

namespace CollegeHomeWork.Tests;

/// <summary>Search and Excel export on a real in-memory SQLite database.</summary>
public class SearchAndExportTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
    private const long Monitor = 2, Anna = 11, Boris = 12;

    private readonly SqliteConnection _connection;
    private readonly TelegramBotContext _db;
    private readonly int _essay, _map, _notebook;

    public SearchAndExportTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new TelegramBotContext(new DbContextOptionsBuilder<TelegramBotContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _db.Users.AddRange(
            new User { TelegramId = Monitor, Username = "monitor", FullName = "Дарья", Role = RoleType.Admin },
            new User { TelegramId = Anna, Username = "anna", FullName = "Анна", Role = RoleType.User },
            new User { TelegramId = Boris, Username = "boris", FullName = "Борис", Role = RoleType.User });
        var history = new ChatGroup { Name = "История" };
        var geo = new ChatGroup { Name = "Всесвит" };
        _db.ChatGroups.AddRange(history, geo);
        _db.SaveChanges();

        var essay = new Message { ChatGroupId = history.Id, FromUserId = Monitor, Text = "Реферат про **Петра I**, 5 страниц. Ёмко и по делу",
            SentAt = Now.AddDays(-5), Deadline = Now.AddDays(-1) };
        var map = new Message { ChatGroupId = geo.Id, FromUserId = Monitor, Text = "Контурная карта Европы",
            SentAt = Now.AddDays(-2), Deadline = Now.AddDays(2) };
        var notebook = new Message { ChatGroupId = history.Id, FromUserId = Monitor, Text = "Сдать тетради",
            SentAt = Now.AddDays(-1) };
        _db.Messages.AddRange(essay, map, notebook);
        _db.SaveChanges();
        (_essay, _map, _notebook) = (essay.Id, map.Id, notebook.Id);

        _db.Attachments.Add(new Attachment { MessageId = map.Id, TelegramFileId = "f1", FileName = "Карта_Европы_шаблон.pdf",
            ContentType = "application/pdf", Size = 100 });
        _db.UserTaskProgresses.AddRange(
            new UserTaskProgress { UserId = Anna, MessageId = essay.Id, IsCompleted = true, CompletedAt = Now.AddDays(-2) },
            new UserTaskProgress { UserId = Anna, MessageId = map.Id, IsCompleted = true, CompletedAt = Now });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // ---------- search ----------

    private Task<IReadOnlyList<SearchHit>> Search(string q, long user = Anna) =>
        new SearchService(_db).SearchAsync(q, user, default);

    [Theory]
    [InlineData("петр")]     // lower case finds "Петра" (SQLite LIKE would miss it)
    [InlineData("ПЕТРА")]
    [InlineData("емко")]     // ё = е
    [InlineData("реферат петр")] // all words, any order
    public async Task FindsTextCaseInsensitive_AnyAlphabet(string q)
    {
        var hits = await Search(q);
        Assert.Equal(new[] { _essay }, hits.Select(h => h.Id));
    }

    [Fact]
    public async Task MarkdownIsNotSearchedAndRemovedFromSnippet()
    {
        Assert.Empty(await Search("**"));
        var hit = (await Search("петра")).Single();
        Assert.DoesNotContain("*", hit.Snippet);
        Assert.Contains("Петра I", hit.Snippet);
    }

    [Fact]
    public async Task FindsByAttachedFileName()
    {
        var hit = (await Search("шаблон")).Single();
        Assert.Equal(_map, hit.Id);
        Assert.Equal("Карта_Европы_шаблон.pdf", hit.MatchedFile);
    }

    [Fact]
    public async Task FindsBySubjectName_NewestFirst()
    {
        Assert.Equal(new[] { _notebook, _essay }, (await Search("история")).Select(h => h.Id));
    }

    [Fact]
    public async Task CompletionIsPersonal()
    {
        Assert.True((await Search("карта", Anna)).Single().IsCompleted);
        Assert.False((await Search("карта", Boris)).Single().IsCompleted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("к")] // shorter than 2 letters
    [InlineData("несуществующее")]
    public async Task ShortOrUnknownQuery_NoResults(string q)
    {
        Assert.Empty(await Search(q));
    }

    [Fact]
    public async Task SearchReturnsKind_LecturesIncluded()
    {
        _db.Messages.Add(new Message { ChatGroupId = _db.ChatGroups.First().Id, FromUserId = Monitor,
            Text = "Лекция про реформы", SentAt = Now, Kind = TaskKind.Lecture });
        _db.SaveChanges();

        var hits = await Search("реформы");
        Assert.Equal("lecture", hits.Single().Kind);
        Assert.Equal("homework", (await Search("карта")).Single().Kind);
    }

    [Fact]
    public void LongTextSnippet_IsCutAroundTheMatch()
    {
        var text = new string('а', 200) + " ИСКОМОЕ слово " + new string('б', 200);
        var snippet = SearchService.Snippet(text, new[] { "искомое" });
        Assert.StartsWith("…", snippet);
        Assert.EndsWith("…", snippet);
        Assert.Contains("ИСКОМОЕ", snippet);
        Assert.True(snippet.Length < 200);
    }

    // ---------- Excel export ----------

    private async Task<XLWorkbook> Export()
    {
        LocalTime.Configure(new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:TimeZone"] = "Europe/Kyiv" }).Build());
        var bytes = await new StatsExportService(new StatsService(_db)).BuildAsync(Now, default);
        return new XLWorkbook(new MemoryStream(bytes));
    }

    [Fact]
    public async Task Export_HasAllSheets_AndTotals()
    {
        using var wb = await Export();

        Assert.Equal(new[] { "Сводка", "Задания", "Студенты", "Таблица сдачи" }, wb.Worksheets.Select(w => w.Name));
        var summary = wb.Worksheet("Сводка");
        Assert.Equal(3, summary.Cell(4, 2).GetValue<int>());  // students: monitor + 2
        Assert.Equal(3, summary.Cell(5, 2).GetValue<int>());  // tasks
        Assert.Equal(22, summary.Cell(6, 2).GetValue<int>()); // 2 done of 3×3 = 22%
        Assert.Equal(2, summary.Cell(7, 2).GetValue<int>());  // essay overdue for Darya and Boris
    }

    [Fact]
    public async Task Export_MatrixMarksDoneAndOverdue()
    {
        using var wb = await Export();
        var ws = wb.Worksheet("Таблица сдачи");

        // Tasks are ordered by deadline (or publish date): essay, notebook (no deadline, sent -1d), map
        var header = Enumerable.Range(2, 3).Select(c => ws.Cell(1, c).GetString()).ToList();
        var essayCol = header.FindIndex(h => h.Contains("Реферат")) + 2;
        var mapCol = header.FindIndex(h => h.Contains("Контурная")) + 2;
        Assert.DoesNotContain("**", header[essayCol - 2]);

        var rows = Enumerable.Range(3, 3).ToDictionary(r => ws.Cell(r, 1).GetString(), r => r);
        Assert.Equal("✓", ws.Cell(rows["Анна"], essayCol).GetString());
        Assert.Equal("✗", ws.Cell(rows["Борис"], essayCol).GetString());  // deadline passed
        Assert.Equal("", ws.Cell(rows["Борис"], mapCol).GetString());     // not done, still time
        Assert.Equal("✓", ws.Cell(rows["Анна"], mapCol).GetString());
    }

    [Fact]
    public async Task Export_DatesInCollegeTimeZone()
    {
        using var wb = await Export();
        var ws = wb.Worksheet("Задания");
        var essayRow = Enumerable.Range(2, 3).First(r => ws.Cell(r, 2).GetString().StartsWith("Реферат"));

        // Deadline 2026-11-11 12:00 UTC = 14:00 in Kyiv
        Assert.Equal(new DateTime(2026, 11, 11, 14, 0, 0), ws.Cell(essayRow, 4).GetDateTime());
        Assert.Equal("просрочено", ws.Cell(essayRow, 8).GetString());
    }

    [Fact]
    public void Export_FileNameHasDate()
    {
        Assert.Equal("Статистика_2026-11-12.xlsx", StatsExportService.FileName(Now));
    }
}
