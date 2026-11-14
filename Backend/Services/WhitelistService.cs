using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TelegramBotApi.Data;
using TelegramBotApi.Models;

namespace TelegramBotApi.Services;

/// <summary>
/// Who may use the bot. Stored in the database table WhitelistEntries and cached in memory,
/// because the check runs on every request. The old whitelist.json used to be reset on every
/// deploy (Render has no persistent disk); now it is only imported once into an empty table.
/// </summary>
public class WhitelistService
{
    public const long PrimaryOwnerId = 8663026875;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WhitelistService> _logger;
    private readonly string? _seedFile;
    private readonly HashSet<long> _cache = new() { PrimaryOwnerId };
    private readonly object _lock = new();

    public WhitelistService(IServiceScopeFactory scopeFactory, ILogger<WhitelistService> logger, string? seedFile = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _seedFile = seedFile;
    }

    /// <summary>
    /// Loads the whitelist after migrations. An empty table is filled once from whitelist.json
    /// and from users already known to the database, so nobody loses access after the upgrade.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TelegramBotContext>();

        if (!await db.WhitelistEntries.AnyAsync(ct))
        {
            var ids = ReadSeedFile().ToHashSet();
            ids.UnionWith(await db.Users.Select(u => u.TelegramId).ToListAsync(ct));
            ids.Add(PrimaryOwnerId);
            db.WhitelistEntries.AddRange(ids.Select(id => new WhitelistEntry { TelegramId = id }));
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Вайтлист перенесён в базу данных: {Count} пользователей", ids.Count);
        }

        var all = await db.WhitelistEntries.Select(e => e.TelegramId).ToListAsync(ct);
        lock (_lock)
        {
            _cache.Clear();
            _cache.UnionWith(all);
            _cache.Add(PrimaryOwnerId);
        }
    }

    public bool Contains(long userId)
    {
        if (userId == PrimaryOwnerId) return true;
        lock (_lock) return _cache.Contains(userId);
    }

    public async Task AddAsync(long userId, CancellationToken ct = default)
    {
        if (Contains(userId) && userId != PrimaryOwnerId) return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TelegramBotContext>();
        if (!await db.WhitelistEntries.AnyAsync(e => e.TelegramId == userId, ct))
        {
            db.WhitelistEntries.Add(new WhitelistEntry { TelegramId = userId });
            await db.SaveChangesAsync(ct);
        }
        lock (_lock) _cache.Add(userId);
    }

    /// <returns>False for the primary owner, who can never be removed.</returns>
    public async Task<bool> RemoveAsync(long userId, CancellationToken ct = default)
    {
        if (userId == PrimaryOwnerId) return false;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TelegramBotContext>();
        var removed = await db.WhitelistEntries.Where(e => e.TelegramId == userId).ExecuteDeleteAsync(ct) > 0;
        lock (_lock) _cache.Remove(userId);
        return removed;
    }

    public IReadOnlyCollection<long> GetAll()
    {
        lock (_lock) return _cache.ToList();
    }

    private IEnumerable<long> ReadSeedFile()
    {
        if (_seedFile == null || !File.Exists(_seedFile)) return Array.Empty<long>();
        try
        {
            return JsonSerializer.Deserialize<List<long>>(File.ReadAllText(_seedFile)) ?? new List<long>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось прочитать {File}", _seedFile);
            return Array.Empty<long>();
        }
    }
}
