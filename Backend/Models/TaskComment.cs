using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramBotApi.Models;

/// <summary>A question or answer under a task, visible to the whole class.</summary>
public class TaskComment
{
    [Key]
    public int Id { get; set; }

    public int MessageId { get; set; }
    [ForeignKey(nameof(MessageId))]
    public Message? Message { get; set; }

    public long UserId { get; set; }
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [Required]
    [MaxLength(1000)]
    public string Text { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
