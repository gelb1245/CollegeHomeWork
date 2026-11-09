using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TelegramBotApi.Services;

public record TelegramValidatedUser(long TelegramId, string? Username, string? FirstName, string? LastName);

/// <summary>
/// Service for verifying cryptographic HMAC-SHA256 signatures of Telegram WebApp initData.
/// Prevents identity spoofing, replay attacks, and unauthorized REST API access.
/// </summary>
public class TelegramAuthService
{
    private readonly string _botToken;
    private readonly ILogger<TelegramAuthService> _logger;
    private readonly byte[] _secretKey;

    public TelegramAuthService(string botToken, ILogger<TelegramAuthService> logger)
    {
        _botToken = botToken ?? throw new ArgumentNullException(nameof(botToken));
        _logger = logger;

        // Step 1 of Telegram Mini App validation:
        // secret_key = HMAC_SHA256(key: "WebAppData", data: bot_token)
        using var hmacSecret = new HMACSHA256(Encoding.UTF8.GetBytes("WebAppData"));
        _secretKey = hmacSecret.ComputeHash(Encoding.UTF8.GetBytes(_botToken));
    }

    /// <summary>
    /// Validates the Telegram WebApp initData string using HMAC-SHA256 signature verification.
    /// Returns true if valid, parses user data and checks auth_date expiration (24h).
    /// </summary>
    public bool ValidateInitData(string initData, out TelegramValidatedUser? user, out string? errorMessage)
    {
        user = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(initData))
        {
            errorMessage = "Строка initData пуста.";
            return false;
        }

        try
        {
            // Parse parameters into key-value pairs
            var pairs = initData.Split('&', StringSplitOptions.RemoveEmptyEntries);
            var queryMap = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var pair in pairs)
            {
                var eqIdx = pair.IndexOf('=');
                if (eqIdx <= 0) continue;

                var key = Uri.UnescapeDataString(pair[..eqIdx]);
                var val = Uri.UnescapeDataString(pair[(eqIdx + 1)..]);
                queryMap[key] = val;
            }

            if (!queryMap.TryGetValue("hash", out var receivedHash) || string.IsNullOrWhiteSpace(receivedHash))
            {
                errorMessage = "Отсутствует параметр hash в initData.";
                return false;
            }

            // Step 2: Build data_check_string (all parameters except 'hash', sorted alphabetically)
            var dataCheckString = string.Join("\n", queryMap
                .Where(kvp => kvp.Key != "hash")
                .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
                .Select(kvp => $"{kvp.Key}={kvp.Value}"));

            // Step 3: Compute HMAC-SHA256(secretKey, dataCheckString)
            using var hmacData = new HMACSHA256(_secretKey);
            var computedHashBytes = hmacData.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString));
            var computedHashHex = Convert.ToHexString(computedHashBytes).ToLowerInvariant();

            // Constant-time comparison to prevent timing attacks
            var expectedBytes = Encoding.UTF8.GetBytes(computedHashHex);
            var actualBytes = Encoding.UTF8.GetBytes(receivedHash.ToLowerInvariant());

            if (!CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
            {
                _logger.LogWarning("Недействительная подпись initData Telegram WebApp");
                errorMessage = "Неверная цифровая подпись Telegram WebApp.";
                return false;
            }

            // Step 4: Verify auth_date (replay attack prevention)
            if (!queryMap.TryGetValue("auth_date", out var authDateStr) || !long.TryParse(authDateStr, out var authDate))
            {
                errorMessage = "Отсутствует или некорректна метка времени auth_date.";
                return false;
            }

            var currentUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // Max allowed age: 24 hours (86400 seconds)
            if (currentUnix - authDate > 86400)
            {
                errorMessage = "Срок действия сессии Telegram WebApp истек (более 24 часов). Пожалуйста, перезапустите Mini App.";
                return false;
            }

            // Allow up to 5 minutes of client clock drift into future
            if (authDate - currentUnix > 300)
            {
                errorMessage = "Некорректное системное время на устройстве клиента.";
                return false;
            }

            // Step 5: Extract User JSON
            if (!queryMap.TryGetValue("user", out var userJson) || string.IsNullOrWhiteSpace(userJson))
            {
                errorMessage = "В initData отсутствуют данные пользователя (user).";
                return false;
            }

            using var doc = JsonDocument.Parse(userJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var idProp) || !idProp.TryGetInt64(out var telegramId))
            {
                errorMessage = "Не найден ID пользователя в объекте user.";
                return false;
            }

            string? username = root.TryGetProperty("username", out var u) ? u.GetString() : null;
            string? firstName = root.TryGetProperty("first_name", out var f) ? f.GetString() : null;
            string? lastName = root.TryGetProperty("last_name", out var l) ? l.GetString() : null;

            user = new TelegramValidatedUser(telegramId, username, firstName, lastName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при валидации initData Telegram");
            errorMessage = "Внутренняя ошибка при проверке подписи.";
            return false;
        }
    }
}
