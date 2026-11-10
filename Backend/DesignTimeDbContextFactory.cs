using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TelegramBotApi.Data;

namespace TelegramBotApi;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TelegramBotContext>
{
    public TelegramBotContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TelegramBotContext>();
        optionsBuilder.UseSqlite("Data Source=telegrambot.db");
        return new TelegramBotContext(optionsBuilder.Options);
    }
}

/// <summary>
/// Used only by "dotnet ef migrations add ... --context PostgresTelegramBotContext".
/// Generating migrations does not connect to the database, so a placeholder connection string is fine.
/// </summary>
public class PostgresDesignTimeDbContextFactory : IDesignTimeDbContextFactory<PostgresTelegramBotContext>
{
    public PostgresTelegramBotContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL") is { } url
            ? DatabaseConfig.ToNpgsqlConnectionString(url)
            : "Host=localhost;Database=collegehomework;Username=postgres";
        var optionsBuilder = new DbContextOptionsBuilder<PostgresTelegramBotContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new PostgresTelegramBotContext(optionsBuilder.Options);
    }
}
