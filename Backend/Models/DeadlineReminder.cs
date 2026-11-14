using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramBotApi.Models;

/// <summary>Which reminder before a deadline: a day before or two hours before.</summary>
public enum ReminderStage
{
    DayBefore = 1,
    HoursBefore = 2
}

/// <summary>A deadline reminder already sent (or skipped) for a user, so it is never sent twice.</summary>
public class DeadlineReminder
{
    [Key]
    public int Id { get; set; }

    public int MessageId { get; set; }
    [ForeignKey(nameof(MessageId))]
    public Message? Message { get; set; }

    public long UserId { get; set; }

    public ReminderStage Stage { get; set; }

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
