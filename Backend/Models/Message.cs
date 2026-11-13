using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramBotApi.Models
{
    /// <summary>
    /// Homework — a task with optional deadline and a "Сдано" mark.
    /// Lecture — study material: no deadline, nothing to hand in, not counted in statistics.
    /// </summary>
    public enum TaskKind
    {
        Homework = 0,
        Lecture = 1
    }

    public class Message
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ChatGroupId { get; set; }
        [ForeignKey(nameof(ChatGroupId))]
        public ChatGroup? Group { get; set; }

        [Required]
        public long FromUserId { get; set; }
        [ForeignKey(nameof(FromUserId))]
        public User? FromUser { get; set; }

        [Required]
        public string Text { get; set; } = string.Empty;

        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        public TaskKind Kind { get; set; } = TaskKind.Homework;

        /// <summary>
        /// Optional deadline for homework submission.
        /// </summary>
        public DateTime? Deadline { get; set; }

        /// <summary>
        /// When the homework was last edited (null if never).
        /// </summary>
        public DateTime? EditedAt { get; set; }

        public List<Attachment> Attachments { get; set; } = new();
    }
}
