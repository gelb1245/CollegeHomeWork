namespace TelegramBotApi.Services;

/// <summary>
/// Prevents the Render free-tier instance from sleeping after 15 minutes of inactivity
/// (a sleeping instance takes 30–60 s to wake up, which makes the Mini App feel very slow).
/// Pings the public /health endpoint every 10 minutes through Render's proxy.
/// Enabled automatically on Render (RENDER_EXTERNAL_URL is set by the platform)
/// or explicitly via the KEEP_ALIVE_URL environment variable.
/// </summary>
public class KeepAliveService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private readonly ILogger<KeepAliveService> _logger;
    private readonly string? _baseUrl;

    public KeepAliveService(ILogger<KeepAliveService> logger)
    {
        _logger = logger;
        _baseUrl = Environment.GetEnvironmentVariable("KEEP_ALIVE_URL")
                   ?? Environment.GetEnvironmentVariable("RENDER_EXTERNAL_URL");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_baseUrl))
        {
            _logger.LogInformation("KeepAlive отключён (не задан RENDER_EXTERNAL_URL / KEEP_ALIVE_URL).");
            return;
        }

        var healthUrl = $"{_baseUrl.TrimEnd('/')}/health";
        _logger.LogInformation("KeepAlive включён: пинг {Url} каждые {Minutes} мин.", healthUrl, Interval.TotalMinutes);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var response = await http.GetAsync(healthUrl, stoppingToken);
                _logger.LogDebug("KeepAlive: {Status}", (int)response.StatusCode);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("KeepAlive: не удалось достучаться до {Url}: {Message}", healthUrl, ex.Message);
            }
        }
    }
}
