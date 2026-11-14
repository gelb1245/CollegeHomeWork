using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramBotApi.Data;
using TelegramBotApi.Models;
using TelegramBotApi.Services;
using Xunit;

namespace CollegeHomeWork.Tests;

/// <summary>The whitelist lives in the database and survives a restart (= a redeploy on Render).</summary>
public class WhitelistServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _services;
    private readonly string _seedFile = Path.GetTempFileName();

    public WhitelistServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _services = new ServiceCollection()
            .AddDbContext<TelegramBotContext>(o => o.UseSqlite(_connection))
            .BuildServiceProvider();
        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TelegramBotContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
        File.Delete(_seedFile);
    }

    private async Task<WhitelistService> StartAsync()
    {
        var service = new WhitelistService(_services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WhitelistService>.Instance, _seedFile);
        await service.InitializeAsync();
        return service;
    }

    private TelegramBotContext Db() => _services.CreateScope().ServiceProvider.GetRequiredService<TelegramBotContext>();

    [Fact]
    public async Task EmptyTable_IsSeededFromJsonAndKnownUsers()
    {
        File.WriteAllText(_seedFile, "[111, 222]");
        using (var db = Db())
        {
            db.Users.Add(new User { TelegramId = 333, Username = "old" });
            db.SaveChanges();
        }

        var whitelist = await StartAsync();

        Assert.True(whitelist.Contains(111));
        Assert.True(whitelist.Contains(333));
        Assert.True(whitelist.Contains(WhitelistService.PrimaryOwnerId));
        Assert.False(whitelist.Contains(999));
    }

    [Fact]
    public async Task AddedAndRemovedUsers_SurviveRestart_JsonIsNotReimported()
    {
        File.WriteAllText(_seedFile, "[111]");
        var first = await StartAsync();
        await first.AddAsync(555);
        await first.RemoveAsync(111);

        // "Redeploy": the file came back from git, a fresh service reads the database
        var second = await StartAsync();

        Assert.True(second.Contains(555));
        Assert.False(second.Contains(111)); // the removed user does not return from whitelist.json
    }

    [Fact]
    public async Task OwnerCannotBeRemoved_AddIsIdempotent()
    {
        var whitelist = await StartAsync();
        Assert.False(await whitelist.RemoveAsync(WhitelistService.PrimaryOwnerId));
        Assert.True(whitelist.Contains(WhitelistService.PrimaryOwnerId));

        await whitelist.AddAsync(777);
        await whitelist.AddAsync(777);
        using var db = Db();
        Assert.Equal(1, db.WhitelistEntries.Count(e => e.TelegramId == 777));
    }
}
