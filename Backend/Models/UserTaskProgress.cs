using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramBotApi.Models
{
    /// <summary>
    /// Tracks individual student completion status for each homework assignment.
    /// </summary>
    public class UserTaskProgress
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public long UserId { get; set; }
        [ForeignKey(nameof(UserId))]
        public User? User { get; set; }

        [Required]
        public int MessageId { get; set; }
        [ForeignKey(nameof(MessageId))]
        public Message? Message { get; set; }

        public bool IsCompleted { get; set; }

        public DateTime? CompletedAt { get; set; }
    }
}
