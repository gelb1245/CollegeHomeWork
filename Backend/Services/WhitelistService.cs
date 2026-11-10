using System.Text.Json;

namespace TelegramBotApi.Services;

public class WhitelistService
{
    private readonly string _filePath;
    private readonly HashSet<long> _whitelist = new();
    private readonly object _lock = new();

    public const long PrimaryOwnerId = 8663026875;

    public WhitelistService(IWebHostEnvironment env)
    {
        _filePath = Path.Combine(env.ContentRootPath, "whitelist.json");
        Load();
    }

    public bool Contains(long userId)
    {
        if (userId == PrimaryOwnerId) return true;
        lock (_lock)
        {
            return _whitelist.Contains(userId);
        }
    }

    public void Add(long userId)
    {
        lock (_lock)
        {
            _whitelist.Add(userId);
            Save();
        }
    }

    public bool Remove(long userId)
    {
        if (userId == PrimaryOwnerId) return false; // Cannot remove primary owner
        lock (_lock)
        {
            var res = _whitelist.Remove(userId);
            Save();
            return res;
        }
    }

    public IReadOnlyCollection<long> GetAll()
    {
        lock (_lock)
        {
            return _whitelist.ToList();
        }
    }

    private void Load()
    {
        lock (_lock)
        {
            if (File.Exists(_filePath))
            {
                try
                {
                    var json = File.ReadAllText(_filePath);
                    var list = JsonSerializer.Deserialize<HashSet<long>>(json);
                    if (list != null)
                    {
                        _whitelist.Clear();
                        foreach (var id in list) _whitelist.Add(id);
                    }
                }
                catch { }
            }
            _whitelist.Add(PrimaryOwnerId);
            Save();
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_whitelist, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch { }
    }
}
