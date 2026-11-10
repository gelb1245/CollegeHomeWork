namespace TelegramBotApi;

/// <summary>
/// Loads KEY=VALUE pairs from a local ".env.local" file (created by `neon init`)
/// into environment variables. Used only in Development: in production (Render)
/// variables come from the hosting environment.
/// Variables that are already set are never overwritten.
/// </summary>
public static class LocalEnvFile
{
    public static void LoadForDevelopment(string fileName = ".env.local")
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (!string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase)) return;

        // Look in the working directory (Backend/) and in the repository root
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "..", fileName),
        };
        var path = candidates.FirstOrDefault(File.Exists);
        if (path == null) return;

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("export ")) line = line["export ".Length..];

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
            {
                value = value[1..^1];
            }

            if (Environment.GetEnvironmentVariable(key) == null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
