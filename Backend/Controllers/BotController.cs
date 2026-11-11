using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramBotApi.Services.Bot;

namespace TelegramBotApi.Controllers;

/// <summary>
/// Telegram webhook endpoint. Active only when BOT_UPDATES_MODE=webhook;
/// updates are processed by the same <see cref="BotUpdateHandler"/> as in polling mode.
/// </summary>
[ApiController]
[Route("api/bot")]
[DisableRateLimiting] // Telegram itself is the only caller; requests are authenticated by the secret token
public class BotController : ControllerBase
{
    private readonly BotUpdateHandler _handler;
    private readonly IConfiguration _config;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<BotController> _log;

    public BotController(BotUpdateHandler handler, IConfiguration config, IHostApplicationLifetime lifetime, ILogger<BotController> log)
    {
        _handler = handler;
        _config = config;
        _lifetime = lifetime;
        _log = log;
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        if (BotUpdatesService.GetMode(_config) != BotUpdatesMode.Webhook) return NotFound();

        var expected = BotUpdatesService.GetWebhookSecret(_config);
        var received = Request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString();
        if (string.IsNullOrEmpty(expected) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(received)))
        {
            _log.LogWarning("Webhook отклонён: неверный секретный токен");
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        // Telegram sends snake_case JSON: parse with the library's own options, not the API's camelCase ones
        Update? update;
        try
        {
            update = await JsonSerializer.DeserializeAsync<Update>(Request.Body, JsonBotAPI.Options, HttpContext.RequestAborted);
        }
        catch (JsonException)
        {
            return BadRequest();
        }
        if (update == null) return BadRequest();

        // Answer Telegram immediately; handle the update in the background
        // (otherwise Telegram retries slow requests and messages get processed twice)
        _ = _handler.HandleAsync(update, _lifetime.ApplicationStopping);
        return Ok();
    }
}
