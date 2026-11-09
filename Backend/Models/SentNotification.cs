using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramBotApi.Models;

/// <summary>
/// Tracks notification messages sent to users' Telegram chats that must self-destruct after 7 hours.
/// </summary>
public class SentNotification
{
    [Key]
    public int Id { get; set; }

    [Required]
    public long TelegramChatId { get; set; }

    [Required]
    public int TelegramMessageId { get; set; }

    [Required]
    public int HomeworkMessageId { get; set; }

    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    [Required]
    public DateTime DeleteAt { get; set; }

    public bool IsDeleted { get; set; } = false;
}
