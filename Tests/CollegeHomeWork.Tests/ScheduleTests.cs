using TelegramBotApi.Controllers;
using Xunit;

namespace CollegeHomeWork.Tests;

public class ScheduleTests
{
    private static LessonDto L(int day, string start, string end, string name = "История") =>
        new(0, 1, name, day, start, end, null);

    // Thursday 2026-11-12
    private static readonly LessonDto[] Week =
    {
        L(4, "09:00", "10:20", "История"), L(4, "10:30", "11:50", "Всесвит"),
        L(5, "09:00", "10:20", "Математика"),
        L(1, "09:00", "10:20", "Физика"),
    };

    [Fact]
    public void BeforeLastClass_ShowsToday()
    {
        var (date, lessons, tomorrow) = ScheduleController.PickDay(Week, new DateTime(2026, 11, 12, 10, 0, 0));
        Assert.Equal(new DateOnly(2026, 11, 12), date);
        Assert.False(tomorrow);
        Assert.Equal(2, lessons.Count);
    }

    [Fact]
    public void AfterClasses_ShowsNextDay()
    {
        var (date, lessons, tomorrow) = ScheduleController.PickDay(Week, new DateTime(2026, 11, 12, 15, 0, 0));
        Assert.Equal(new DateOnly(2026, 11, 13), date);
        Assert.True(tomorrow);
        Assert.Equal("Математика", lessons.Single().GroupName);
    }

    [Fact]
    public void Friday_Evening_SkipsWeekendToMonday()
    {
        var (date, lessons, _) = ScheduleController.PickDay(Week, new DateTime(2026, 11, 13, 18, 0, 0));
        Assert.Equal(new DateOnly(2026, 11, 16), date);
        Assert.Equal("Физика", lessons.Single().GroupName);
    }

    [Fact]
    public void EmptyTimetable_NoLessons()
    {
        var (_, lessons, _) = ScheduleController.PickDay(Array.Empty<LessonDto>(), new DateTime(2026, 11, 12, 10, 0, 0));
        Assert.Empty(lessons);
    }

    [Fact]
    public void CommentNotice_EscapesAndShortens()
    {
        var text = CommentsController.BuildNotice(false, "Анна_К", "История", new string('а', 200), "Сколько *страниц*?");
        Assert.StartsWith("❓", text);
        Assert.Contains("Анна\\_К", text);
        Assert.Contains("…", text);
    }
}
