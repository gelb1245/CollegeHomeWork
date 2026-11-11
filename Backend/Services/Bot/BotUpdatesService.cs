using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace TelegramBotApi.Services.Bot;

public enum BotUpdatesMode
{
    /// <summary>The app asks Telegram for new messages (works anywhere, the default).</summary>
    Polling,
    /// <summary>Telegram pushes updates to POST /api/bot/webhook (needs a public HTTPS URL).</summary>
    Webhook,
    /// <summary>Do not receive updates (local development with the production token).</summary>
    Off
}

/// <summary>
/// Starts the bot in exactly one update mode, chosen by BOT_UPDATES_MODE / Telegram:UpdatesMode
/// (polling | webhook | off). Running two receivers for one token makes Telegram return errors
/// and lose messages, so only one is ever active.
/// </summary>
public class BotUpdatesService : BackgroundService
{
    private static readonly UpdateType[] AllowedUpdates = { UpdateType.Message, UpdateType.CallbackQuery };

    private readonly ITelegramBotClient _bot;
    private readonly BotUpdateHandler _handler;
    private readonly IConfiguration _config;
    private readonly ILogger<BotUpdatesService> _logger;

    public BotUpdatesService(ITelegramBotClient bot, BotUpdateHandler handler, IConfiguration config, ILogger<BotUpdatesService> logger)
    {
        _bot = bot;
        _handler = handler;
        _config = config;
        _logger = logger;
    }

    public static BotUpdatesMode GetMode(IConfiguration config)
    {
        var raw = Environment.GetEnvironmentVariable("BOT_UPDATES_MODE") ?? config["Telegram:UpdatesMode"];
        return Enum.TryParse<BotUpdatesMode>(raw, ignoreCase: true, out var mode) ? mode : BotUpdatesMode.Polling;
    }

    /// <summary>Secret that Telegram sends with every webhook call (X-Telegram-Bot-Api-Secret-Token).</summary>
    public static string? GetWebhookSecret(IConfiguration config) =>
        Environment.GetEnvironmentVariable("TELEGRAM_WEBHOOK_SECRET") ?? config["Telegram:WebhookSecretToken"];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var mode = GetMode(_config);
        if (mode == BotUpdatesMode.Off)
        {
            _logger.LogInformation("Получение обновлений бота отключено (BOT_UPDATES_MODE=off).");
            return;
        }

        try
        {
            var me = await _bot.GetMe(stoppingToken);
            _logger.LogInformation("Бот @{Username} запущен в режиме {Mode}", me.Username, mode);
            await SetupMenuButtonAsync(stoppingToken);

            if (mode == BotUpdatesMode.Webhook)
            {
                await SetupWebhookAsync(stoppingToken);
                return; // updates arrive via BotController
            }

            // Polling and webhook cannot work together: remove a webhook left from before
            await _bot.DeleteWebhook(dropPendingUpdates: false, cancellationToken: stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Не удалось подключиться к Telegram API. Проверьте токен бота.");
            return;
        }

        await PollAsync(stoppingToken);
    }

    private async Task PollAsync(CancellationToken stoppingToken)
    {
        var offset = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await _bot.GetUpdates(offset, timeout: 25, allowedUpdates: AllowedUpdates, cancellationToken: stoppingToken);
                foreach (var update in updates)
                {
                    offset = update.Id + 1;
                    _ = _handler.HandleAsync(update, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения обновлений Telegram");
                await Task.Delay(3000, stoppingToken);
            }
        }
    }

    private async Task SetupWebhookAsync(CancellationToken ct)
    {
        var baseUrl = MiniAppLink.GetUrl(_config)
                      ?? throw new InvalidOperationException("Для режима webhook нужен публичный HTTPS-адрес в Telegram:WebAppUrl.");
        var secret = GetWebhookSecret(_config);
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Для режима webhook задайте TELEGRAM_WEBHOOK_SECRET.");

        var url = $"{baseUrl.TrimEnd('/')}/api/bot/webhook";
        await _bot.SetWebhook(url, allowedUpdates: AllowedUpdates, secretToken: secret, cancellationToken: ct);
        _logger.LogInformation("Webhook установлен: {Url}", url);
    }

    private async Task SetupMenuButtonAsync(CancellationToken ct)
    {
        var url = MiniAppLink.GetUrl(_config);
        if (url == null) return;
        try
        {
            await _bot.SetChatMenuButton(
                menuButton: new MenuButtonWebApp { Text = "📱 Открыть ДЗ", WebApp = new WebAppInfo { Url = url } },
                cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Не удалось настроить кнопку меню Mini App: {Error}", ex.Message);
        }
    }
}
