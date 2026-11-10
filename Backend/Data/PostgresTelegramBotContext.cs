using Microsoft.EntityFrameworkCore;

namespace TelegramBotApi.Data;

/// <summary>
/// PostgreSQL flavour of <see cref="TelegramBotContext"/>.
/// It exists only so that PostgreSQL has its own migrations (Migrations/Postgres),
/// separate from the SQLite ones. The model itself is inherited unchanged.
/// </summary>
public class PostgresTelegramBotContext : TelegramBotContext
{
    static PostgresTelegramBotContext()
    {
        // Store DateTime as "timestamp without time zone" (like SQLite did) instead of requiring
        // DateTimeKind.Utc on every value. The app already writes UTC everywhere.
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }

    public PostgresTelegramBotContext(DbContextOptions<PostgresTelegramBotContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Telegram ID comes from Telegram — the database must never generate it
        modelBuilder.Entity<Models.User>()
            .Property(u => u.TelegramId)
            .ValueGeneratedNever();
    }
}
