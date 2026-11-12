using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramBotApi.Models;

/// <summary>
/// Personal notification preferences. A user without a row gets the defaults
/// (everything on, no quiet hours) — the same behaviour as before settings existed.
/// </summary>
public class NotificationSettings
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long UserId { get; set; }
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    /// <summary>Receive a message when a new task is published.</summary>
    public bool NewTasks { get; set; } = true;

    /// <summary>Delete the notification from the chat after 24 hours.</summary>
    public bool AutoDelete { get; set; } = true;

    /// <summary>During quiet hours messages still arrive, but without sound.</summary>
    public bool QuietHoursEnabled { get; set; }

    /// <summary>Quiet hours start, minutes after midnight in the college time zone (default 22:00).</summary>
    public int QuietFromMinutes { get; set; } = 22 * 60;

    /// <summary>Quiet hours end, minutes after midnight (default 08:00).</summary>
    public int QuietToMinutes { get; set; } = 8 * 60;
}

/// <summary>A subject the user does not want notifications about.</summary>
public class MutedSubject
{
    public long UserId { get; set; }
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    public int ChatGroupId { get; set; }
    [ForeignKey(nameof(ChatGroupId))]
    public ChatGroup? Group { get; set; }
}
