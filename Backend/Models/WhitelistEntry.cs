using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramBotApi.Models;

/// <summary>
/// A Telegram user allowed to use the bot. Lives in the database (not in whitelist.json),
/// so access survives redeploys on hosts with an ephemeral disk such as Render.
/// </summary>
public class WhitelistEntry
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long TelegramId { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
