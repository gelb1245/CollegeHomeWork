using Telegram.Bot;
using Telegram.Bot.Types;
using TelegramBotApi.Models;

namespace TelegramBotApi.Services;

/// <summary>
/// Stores homework attachments (photos, PDF) on Telegram servers for free:
/// the bot uploads the file into a chat and we keep only its file_id.
/// Files are later streamed back through the Bot API.
/// </summary>
public class AttachmentStorage
{
    public const int MaxFilesPerMessage = 5;

    /// <summary>Bot API can download files up to 20 MB.</summary>
    public const long MaxFileSize = 20 * 1024 * 1024;

    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/gif"] = ".gif",
        ["application/pdf"] = ".pdf",
    };

    private readonly ITelegramBotClient _bot;
    private readonly IConfiguration _config;
    private readonly ILogger<AttachmentStorage> _logger;

    public AttachmentStorage(ITelegramBotClient bot, IConfiguration config, ILogger<AttachmentStorage> logger)
    {
        _bot = bot;
        _config = config;
        _logger = logger;
    }

    public static bool IsImage(string contentType) => contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Validates size and real file type (by magic bytes, not by the client-provided name).
    /// Returns an error message or null if the file is acceptable.
    /// </summary>
    public static async Task<string?> ValidateAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0) return $"Файл «{file.FileName}» пустой.";
        if (file.Length > MaxFileSize) return $"Файл «{file.FileName}» больше 20 МБ.";

        var detected = await DetectContentTypeAsync(file, ct);
        if (detected == null) return $"Файл «{file.FileName}»: поддерживаются только фото (JPG, PNG, WEBP, GIF) и PDF.";
        return null;
    }

    /// <summary>
    /// Uploads a file to Telegram and returns an (unsaved) Attachment entity.
    /// Storage chat: TELEGRAM_STORAGE_CHAT_ID / Telegram:StorageChatId if set, otherwise the uploader's own chat with the bot.
    /// </summary>
    public async Task<Attachment> UploadAsync(IFormFile file, long uploaderChatId, string caption, CancellationToken ct)
    {
        var contentType = await DetectContentTypeAsync(file, ct)
                          ?? throw new InvalidOperationException("Неподдерживаемый тип файла.");
        var fileName = SanitizeFileName(file.FileName, AllowedTypes[contentType]);
        var chatId = ResolveStorageChatId(uploaderChatId);

        await using var stream = file.OpenReadStream();
        var sent = await _bot.SendDocument(
            chatId: chatId,
            document: InputFile.FromStream(stream, fileName),
            caption: caption,
            disableContentTypeDetection: true,
            disableNotification: true,
            cancellationToken: ct);

        var fileId = sent.Document?.FileId ?? sent.Animation?.FileId
                     ?? throw new InvalidOperationException("Telegram не вернул file_id загруженного файла.");

        _logger.LogInformation("Файл {FileName} ({Size} байт) сохранён в Telegram (чат {ChatId})", fileName, file.Length, chatId);

        return new Attachment
        {
            TelegramFileId = fileId,
            FileName = fileName,
            ContentType = contentType,
            Size = file.Length,
            UploadedAt = DateTime.UtcNow
        };
    }

    /// <summary>Streams a stored file from Telegram into the destination stream.</summary>
    public async Task DownloadAsync(string telegramFileId, Stream destination, CancellationToken ct)
    {
        var file = await _bot.GetFile(telegramFileId, ct);
        if (string.IsNullOrEmpty(file.FilePath))
            throw new InvalidOperationException("Telegram не вернул путь к файлу.");
        await _bot.DownloadFile(file.FilePath, destination, ct);
    }

    private long ResolveStorageChatId(long uploaderChatId)
    {
        var configured = Environment.GetEnvironmentVariable("TELEGRAM_STORAGE_CHAT_ID") ?? _config["Telegram:StorageChatId"];
        return long.TryParse(configured, out var id) && id != 0 ? id : uploaderChatId;
    }

    private static async Task<string?> DetectContentTypeAsync(IFormFile file, CancellationToken ct)
    {
        var header = new byte[12];
        await using var s = file.OpenReadStream();
        var read = await s.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        if (read < 4) return null;

        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return "image/jpeg";
        if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47) return "image/png";
        if (header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46) return "image/gif";
        if (header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46) return "application/pdf";
        if (read >= 12 && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
            header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50) return "image/webp";
        return null;
    }

    private static string SanitizeFileName(string? name, string extension)
    {
        var baseName = Path.GetFileNameWithoutExtension(name ?? "");
        var invalid = Path.GetInvalidFileNameChars();
        baseName = new string(baseName.Where(c => !invalid.Contains(c) && !char.IsControl(c)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "file";
        if (baseName.Length > 100) baseName = baseName[..100];
        return baseName + extension;
    }
}
