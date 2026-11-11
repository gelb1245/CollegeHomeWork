using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramBotApi.Models;

/// <summary>
/// A file (photo or PDF) attached to a homework message.
/// The file itself is stored on Telegram servers; only its file_id is kept here.
/// </summary>
public class Attachment
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int MessageId { get; set; }
    [ForeignKey(nameof(MessageId))]
    public Message? Message { get; set; }

    /// <summary>Telegram file_id used to download the file via Bot API.</summary>
    [Required, MaxLength(255)]
    public string TelegramFileId { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public long Size { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
