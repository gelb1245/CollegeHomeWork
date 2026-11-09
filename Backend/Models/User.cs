using System.ComponentModel.DataAnnotations;

namespace TelegramBotApi.Models
{
    public enum RoleType
    {
        Developer,
        Admin,
        User
    }

    public class User
    {
        [Key]
        public long TelegramId { get; set; }

        [Required]
        public string Username { get; set; } = string.Empty;

        public string? FullName { get; set; }

        public string? PhoneNumber { get; set; }

        public RoleType Role { get; set; } = RoleType.User;
    }
}
