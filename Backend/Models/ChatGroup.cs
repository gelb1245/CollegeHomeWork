using System.ComponentModel.DataAnnotations;

namespace TelegramBotApi.Models
{
    public class ChatGroup
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty; // e.g. "History", "Homework"

        // Optional description shown in the UI
        public string? Description { get; set; }
    }
}