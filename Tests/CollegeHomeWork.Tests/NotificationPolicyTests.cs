using TelegramBotApi.Models;
using TelegramBotApi.Services;
using Xunit;

namespace CollegeHomeWork.Tests;

public class NotificationPolicyTests
{
    private static readonly IReadOnlySet<int> NoMutes = new HashSet<int>();

    private static NotificationSettings Quiet(string from, string to)
    {
        NotificationPolicy.TryParseTime(from, out var f);
        NotificationPolicy.TryParseTime(to, out var t);
        return new NotificationSettings { QuietHoursEnabled = true, QuietFromMinutes = f, QuietToMinutes = t };
    }

    [Fact]
    public void NoSettings_MeansEverythingOn()
    {
        Assert.True(NotificationPolicy.ShouldNotify(null, NoMutes, 1));
        Assert.True(NotificationPolicy.ShouldAutoDelete(null));
        Assert.False(NotificationPolicy.IsQuietTime(null, new TimeOnly(3, 0)));
    }

    [Fact]
    public void NewTasksOff_NoNotifications()
    {
        Assert.False(NotificationPolicy.ShouldNotify(new NotificationSettings { NewTasks = false }, NoMutes, 1));
    }

    [Fact]
    public void MutedSubject_OnlyThatSubjectIsSilenced()
    {
        var muted = new HashSet<int> { 2 };
        Assert.False(NotificationPolicy.ShouldNotify(null, muted, 2));
        Assert.True(NotificationPolicy.ShouldNotify(null, muted, 1));
    }

    [Theory]
    [InlineData("23:30", true)]   // after start
    [InlineData("22:00", true)]   // start is inclusive
    [InlineData("03:00", true)]   // after midnight
    [InlineData("07:59", true)]
    [InlineData("08:00", false)]  // end is exclusive
    [InlineData("12:00", false)]
    [InlineData("21:59", false)]
    public void QuietHours_OverMidnight(string time, bool quiet)
    {
        Assert.Equal(quiet, NotificationPolicy.IsQuietTime(Quiet("22:00", "08:00"), TimeOnly.Parse(time)));
    }

    [Theory]
    [InlineData("13:00", true)]
    [InlineData("14:59", true)]
    [InlineData("15:00", false)]
    [InlineData("12:59", false)]
    public void QuietHours_WithinOneDay(string time, bool quiet)
    {
        Assert.Equal(quiet, NotificationPolicy.IsQuietTime(Quiet("13:00", "15:00"), TimeOnly.Parse(time)));
    }

    [Fact]
    public void QuietHours_Disabled_OrEmptyRange_NeverQuiet()
    {
        var disabled = Quiet("22:00", "08:00");
        disabled.QuietHoursEnabled = false;
        Assert.False(NotificationPolicy.IsQuietTime(disabled, new TimeOnly(23, 0)));
        Assert.False(NotificationPolicy.IsQuietTime(Quiet("10:00", "10:00"), new TimeOnly(10, 0)));
    }

    [Theory]
    [InlineData("22:00", 1320)]
    [InlineData("08:05", 485)]
    [InlineData("00:00", 0)]
    public void TimeParsing(string value, int minutes)
    {
        Assert.True(NotificationPolicy.TryParseTime(value, out var parsed));
        Assert.Equal(minutes, parsed);
        Assert.Equal(value, NotificationPolicy.FormatMinutes(parsed));
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("8:00pm")]
    [InlineData("")]
    [InlineData(null)]
    public void InvalidTime_IsRejected(string? value)
    {
        Assert.False(NotificationPolicy.TryParseTime(value, out _));
    }
}
