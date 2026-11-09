using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramBotApi.Models
{
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
    }
}