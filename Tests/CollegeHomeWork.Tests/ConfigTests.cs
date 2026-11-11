using Microsoft.Extensions.Configuration;
using Npgsql;
using TelegramBotApi.Data;
using TelegramBotApi.Services;
using TelegramBotApi.Services.Bot;
using Xunit;

namespace CollegeHomeWork.Tests;

public class DatabaseConfigTests
{
    [Theory]
    [InlineData("postgresql://u:p@host/db", true)]
    [InlineData("postgres://u:p@host/db", true)]
    [InlineData("Host=localhost;Database=db", true)]
    [InlineData("Data Source=telegrambot.db", false)]
    [InlineData("Data Source=/var/data/app.db;Cache=Shared", false)]
    public void ProviderIsDetected(string connectionString, bool isPostgres)
    {
        Assert.Equal(isPostgres, DatabaseConfig.IsPostgres(connectionString));
    }

    [Fact]
    public void NeonUrl_IsConvertedForNpgsql()
    {
        // Same shape as the string Neon gives in its console (password with URL-encoded characters)
        const string url = "postgresql://neondb_owner:p%40ss%2Fw0rd@ep-cool-name-123-pooler.eu-central-1.aws.neon.tech/neondb?sslmode=require&channel_binding=require";

        var b = new NpgsqlConnectionStringBuilder(DatabaseConfig.ToNpgsqlConnectionString(url));

        Assert.Equal("ep-cool-name-123-pooler.eu-central-1.aws.neon.tech", b.Host);
        Assert.Equal(5432, b.Port);
        Assert.Equal("neondb", b.Database);
        Assert.Equal("neondb_owner", b.Username);
        Assert.Equal("p@ss/w0rd", b.Password);
        Assert.Equal(SslMode.Require, b.SslMode);
        Assert.Equal(ChannelBinding.Require, b.ChannelBinding);
    }

    [Fact]
    public void KeyValueString_IsLeftAsIs()
    {
        const string cs = "Host=localhost;Port=5433;Database=test;Username=postgres";
        Assert.Equal(cs, DatabaseConfig.ToNpgsqlConnectionString(cs));
    }
}

public class LocalTimeTests
{
    [Fact]
    public void UtcIsConvertedToKyivTime()
    {
        LocalTime.Configure(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["App:TimeZone"] = "Europe/Kyiv" }).Build());

        // November: Kyiv is UTC+2. A deadline "tomorrow 23:59" is stored as 21:59 UTC.
        var local = LocalTime.FromUtc(new DateTime(2026, 11, 12, 21, 59, 0, DateTimeKind.Unspecified));

        Assert.Equal(new DateTime(2026, 11, 12, 23, 59, 0), local);
    }
}

public class MiniAppLinkTests
{
    private static IConfiguration Config(string? url) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Telegram:WebAppUrl"] = url }).Build();

    [Theory]
    [InlineData("https://collegehomework-308h.onrender.com", true)]
    [InlineData("http://example.com", false)]          // Telegram requires HTTPS
    [InlineData("https://localhost:5001", false)]      // not reachable from Telegram
    [InlineData("https://127.0.0.1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyPublicHttpsUrlsOpenTheMiniApp(string? url, bool valid)
    {
        Assert.Equal(valid, MiniAppLink.GetUrl(Config(url)) != null);
        Assert.Equal(valid, MiniAppLink.Button(Config(url), "Open") != null);
    }
}
