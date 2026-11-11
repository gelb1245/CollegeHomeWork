using System.Security.Cryptography;
using System.Text;

namespace TelegramBotApi.Services;

/// <summary>
/// Creates short-lived signed links to attachments.
/// &lt;img src&gt; and links opened in the browser cannot send the Telegram Authorization header,
/// so access is granted by an HMAC signature instead. Links are handed out only to authorized users
/// (inside the messages list) and expire after <see cref="Lifetime"/>.
/// </summary>
public class AttachmentUrlSigner
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(6);

    private readonly byte[] _key;

    public AttachmentUrlSigner(string botToken)
    {
        // Separate key derived from the bot token (never expose the token itself)
        _key = SHA256.HashData(Encoding.UTF8.GetBytes("attachment-links:" + botToken));
    }

    public string CreateUrl(int attachmentId)
    {
        var expires = DateTimeOffset.UtcNow.Add(Lifetime).ToUnixTimeSeconds();
        return $"/api/files/{attachmentId}?exp={expires}&sig={Sign(attachmentId, expires)}";
    }

    public bool IsValid(int attachmentId, long expires, string? signature)
    {
        if (string.IsNullOrEmpty(signature)) return false;
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expires) return false;

        var expected = Encoding.ASCII.GetBytes(Sign(attachmentId, expires));
        var actual = Encoding.ASCII.GetBytes(signature);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private string Sign(int attachmentId, long expires)
    {
        var hash = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{attachmentId}:{expires}"));
        return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }
}
