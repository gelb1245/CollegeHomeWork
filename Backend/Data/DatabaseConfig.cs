using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TelegramBotApi.Data;

/// <summary>
/// Chooses the database provider from configuration:
/// <list type="bullet">
/// <item>PostgreSQL — if DATABASE_URL or ConnectionStrings:Default is a PostgreSQL URL
/// (postgresql://user:pass@host/db?sslmode=require, as Neon/Supabase/Render give it) or contains "Host=".</item>
/// <item>SQLite (local file telegrambot.db) — otherwise.</item>
/// </list>
/// </summary>
public static class DatabaseConfig
{
    private const string DefaultSqlite = "Data Source=telegrambot.db;Cache=Shared;Mode=ReadWriteCreate;Default Timeout=15";

    public static void AddBotDatabase(this IServiceCollection services, IConfiguration config, ILogger? logger = null)
    {
        var raw = Environment.GetEnvironmentVariable("DATABASE_URL")
                  ?? config.GetConnectionString("Default")
                  ?? DefaultSqlite;

        if (IsPostgres(raw))
        {
            var cs = ToNpgsqlConnectionString(raw);
            services.AddDbContext<TelegramBotContext, PostgresTelegramBotContext>(opt =>
                opt.UseNpgsql(cs, npgsql => npgsql.EnableRetryOnFailure(3)));
            logger?.LogInformation("База данных: PostgreSQL ({Host})", new NpgsqlConnectionStringBuilder(cs).Host);
        }
        else
        {
            services.AddDbContext<TelegramBotContext>(opt => opt.UseSqlite(raw));
            logger?.LogInformation("База данных: SQLite");
        }
    }

    public static bool IsPostgres(string connectionString) =>
        connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
        connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) ||
        connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Npgsql does not accept URL-style connection strings, so convert
    /// postgresql://user:pass@host:port/db?sslmode=require into key=value form.
    /// </summary>
    public static string ToNpgsqlConnectionString(string raw)
    {
        if (!raw.Contains("://")) return raw;

        var uri = new Uri(raw);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            SslMode = SslMode.Prefer,
        };

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(kv[0]).ToLowerInvariant();
            var value = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
            switch (key)
            {
                case "sslmode":
                    if (Enum.TryParse<SslMode>(value.Replace("-", ""), ignoreCase: true, out var mode))
                        builder.SslMode = mode;
                    break;
                case "channel_binding":
                    if (Enum.TryParse<ChannelBinding>(value, ignoreCase: true, out var cb))
                        builder.ChannelBinding = cb;
                    break;
                // Other URL options (e.g. options=endpoint%3D...) are not needed by Npgsql
            }
        }

        return builder.ConnectionString;
    }
}
