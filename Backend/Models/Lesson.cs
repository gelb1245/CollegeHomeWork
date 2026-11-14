using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramBotApi.Models;

/// <summary>One class in the weekly timetable.</summary>
public class Lesson
{
    [Key]
    public int Id { get; set; }

    public int ChatGroupId { get; set; }
    [ForeignKey(nameof(ChatGroupId))]
    public ChatGroup? Group { get; set; }

    /// <summary>1 = Monday … 7 = Sunday.</summary>
    public int DayOfWeek { get; set; }

    /// <summary>Start and end, minutes after midnight in the college time zone.</summary>
    public int StartMinutes { get; set; }
    public int EndMinutes { get; set; }

    [MaxLength(50)]
    public string? Room { get; set; }
}
