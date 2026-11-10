using Microsoft.EntityFrameworkCore;

namespace TelegramBotApi.Data;

/// <summary>
/// Brings the SQLite database to the current schema using EF Core migrations.
/// Also "adopts" legacy databases that were created earlier via EnsureCreated() and raw SQL,
/// so that MigrateAsync() does not try to re-create tables that already exist.
/// </summary>
public static class DatabaseInitializer
{
    // The first migration whose changes used to be applied by raw SQL in Program.cs.
    private const string LegacyPatchedMigration = "20261110181300_AddDeadlineProgressNotifications";
    private const string EfProductVersion = "8.0.0";

    public static async Task InitializeAsync(TelegramBotContext db, ILogger logger, CancellationToken ct = default)
    {
        var usersTableExists = await TableExistsAsync(db, "Users", ct);
        var historyTableExists = await TableExistsAsync(db, "__EFMigrationsHistory", ct);

        if (usersTableExists)
        {
            var allMigrations = db.Database.GetMigrations().ToList();

            if (!historyTableExists)
            {
                // Database was created by EnsureCreated(): schema exists, but no migration history.
                logger.LogWarning("Обнаружена БД без истории миграций (EnsureCreated). Выполняется перенос на миграции.");
                await db.Database.ExecuteSqlRawAsync(
                    "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (" +
                    "\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK___EFMigrationsHistory\" PRIMARY KEY, " +
                    "\"ProductVersion\" TEXT NOT NULL);", ct);

                foreach (var id in allMigrations.TakeWhile(m => string.CompareOrdinal(m, LegacyPatchedMigration) < 0))
                {
                    await MarkAppliedAsync(db, id, ct);
                }
            }

            var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToHashSet();
            if (!applied.Contains(LegacyPatchedMigration))
            {
                logger.LogWarning("Применяется совместимость со старой схемой (Deadline, UserTaskProgresses, SentNotifications).");
                await ApplyLegacyPatchAsync(db, ct);
                await MarkAppliedAsync(db, LegacyPatchedMigration, ct);
            }
        }

        await db.Database.MigrateAsync(ct);

        // WAL mode & busy timeout to prevent "database is locked" errors under load
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;", ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA synchronous = NORMAL;", ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout = 5000;", ct);
    }

    /// <summary>
    /// Idempotently creates the objects of <see cref="LegacyPatchedMigration"/> if they are missing.
    /// </summary>
    private static async Task ApplyLegacyPatchAsync(TelegramBotContext db, CancellationToken ct)
    {
        if (!await ColumnExistsAsync(db, "Messages", "Deadline", ct))
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Messages\" ADD COLUMN \"Deadline\" TEXT NULL;", ct);
        }

        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ""UserTaskProgresses"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_UserTaskProgresses"" PRIMARY KEY AUTOINCREMENT,
                ""UserId"" INTEGER NOT NULL,
                ""MessageId"" INTEGER NOT NULL,
                ""IsCompleted"" INTEGER NOT NULL,
                ""CompletedAt"" TEXT NULL,
                CONSTRAINT ""FK_UserTaskProgresses_Messages_MessageId"" FOREIGN KEY (""MessageId"") REFERENCES ""Messages"" (""Id"") ON DELETE CASCADE,
                CONSTRAINT ""FK_UserTaskProgresses_Users_UserId"" FOREIGN KEY (""UserId"") REFERENCES ""Users"" (""TelegramId"") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ""IX_UserTaskProgresses_MessageId"" ON ""UserTaskProgresses"" (""MessageId"");
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_UserTaskProgresses_UserId_MessageId"" ON ""UserTaskProgresses"" (""UserId"", ""MessageId"");

            CREATE TABLE IF NOT EXISTS ""SentNotifications"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_SentNotifications"" PRIMARY KEY AUTOINCREMENT,
                ""TelegramChatId"" INTEGER NOT NULL,
                ""TelegramMessageId"" INTEGER NOT NULL,
                ""HomeworkMessageId"" INTEGER NOT NULL,
                ""SentAt"" TEXT NOT NULL,
                ""DeleteAt"" TEXT NOT NULL,
                ""IsDeleted"" INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ""IX_SentNotifications_HomeworkMessageId"" ON ""SentNotifications"" (""HomeworkMessageId"");
            CREATE INDEX IF NOT EXISTS ""IX_SentNotifications_IsDeleted_DeleteAt"" ON ""SentNotifications"" (""IsDeleted"", ""DeleteAt"");
        ", ct);
    }

    private static Task MarkAppliedAsync(TelegramBotContext db, string migrationId, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(
            "INSERT OR IGNORE INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({0}, {1});",
            new object[] { migrationId, EfProductVersion }, ct);

    private static async Task<bool> TableExistsAsync(TelegramBotContext db, string table, CancellationToken ct)
    {
        var count = await db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE type = 'table' AND name = {0}", table)
            .SingleAsync(ct);
        return count > 0;
    }

    private static async Task<bool> ColumnExistsAsync(TelegramBotContext db, string table, string column, CancellationToken ct)
    {
        var count = await db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM pragma_table_info({0}) WHERE name = {1}", table, column)
            .SingleAsync(ct);
        return count > 0;
    }
}
