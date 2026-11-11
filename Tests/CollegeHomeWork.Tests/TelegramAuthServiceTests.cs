using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramBotApi.Services;
using Xunit;

namespace CollegeHomeWork.Tests;

/// <summary>
/// Telegram Mini App initData signature check — the core of the app's security:
/// every API request is trusted only if this validation passes.
/// </summary>
public class TelegramAuthServiceTests
{
    private const string BotToken = "123456:TEST-token-for-unit-tests";
    private const string UserJson = "{\"id\":8663026875,\"first_name\":\"Gleb\",\"username\":\"vtmmq\"}";

    private static TelegramAuthService CreateService(string token = BotToken) =>
        new(token, NullLogger<TelegramAuthService>.Instance);

    /// <summary>Builds initData exactly like Telegram does (HMAC-SHA256 with the "WebAppData" key).</summary>
    private static string BuildInitData(string token, string userJson, long authDate, bool includeHash = true)
    {
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_date"] = authDate.ToString(),
            ["query_id"] = "AAH-test",
            ["user"] = userJson
        };
        var dataCheckString = string.Join("\n", fields.Select(kv => $"{kv.Key}={kv.Value}"));
        var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(token));
        var hash = Convert.ToHexString(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(dataCheckString))).ToLowerInvariant();

        var query = string.Join("&", fields.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        return includeHash ? $"{query}&hash={hash}" : query;
    }

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    [Fact]
    public void ValidInitData_IsAccepted_AndUserIsParsed()
    {
        var ok = CreateService().ValidateInitData(BuildInitData(BotToken, UserJson, Now), out var user, out var error);

        Assert.True(ok, error);
        Assert.NotNull(user);
        Assert.Equal(8663026875, user!.TelegramId);
        Assert.Equal("vtmmq", user.Username);
    }

    [Fact]
    public void ChangedUserId_IsRejected()
    {
        // Attacker takes a valid initData and swaps the user id to impersonate someone else
        var initData = BuildInitData(BotToken, UserJson, Now).Replace("8663026875", "1645991235");

        Assert.False(CreateService().ValidateInitData(initData, out _, out _));
    }

    [Fact]
    public void SignatureFromAnotherBot_IsRejected()
    {
        var initData = BuildInitData("999:other-bot-token", UserJson, Now);

        Assert.False(CreateService().ValidateInitData(initData, out _, out _));
    }

    [Fact]
    public void SessionOlderThan24Hours_IsRejected()
    {
        var initData = BuildInitData(BotToken, UserJson, Now - 25 * 3600);

        Assert.False(CreateService().ValidateInitData(initData, out _, out var error));
        Assert.Contains("24", error);
    }

    [Fact]
    public void AuthDateFarInFuture_IsRejected()
    {
        var initData = BuildInitData(BotToken, UserJson, Now + 3600);

        Assert.False(CreateService().ValidateInitData(initData, out _, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("user=%7B%7D&auth_date=1")]
    public void MalformedInitData_IsRejected(string initData)
    {
        Assert.False(CreateService().ValidateInitData(initData, out _, out _));
    }

    [Fact]
    public void MissingHash_IsRejected()
    {
        var initData = BuildInitData(BotToken, UserJson, Now, includeHash: false);

        Assert.False(CreateService().ValidateInitData(initData, out _, out _));
    }
}
