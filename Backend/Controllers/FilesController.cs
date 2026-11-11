using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TelegramBotApi.Data;
using TelegramBotApi.Services;

namespace TelegramBotApi.Controllers;

/// <summary>
/// Serves homework attachments via short-lived signed links (see <see cref="AttachmentUrlSigner"/>).
/// </summary>
[ApiController]
[Route("api/files")]
public class FilesController : ControllerBase
{
    private readonly TelegramBotContext _db;
    private readonly AttachmentStorage _storage;
    private readonly AttachmentUrlSigner _signer;
    private readonly ILogger<FilesController> _log;

    public FilesController(TelegramBotContext db, AttachmentStorage storage, AttachmentUrlSigner signer, ILogger<FilesController> log)
    {
        _db = db;
        _storage = storage;
        _signer = signer;
        _log = log;
    }

    [HttpGet("{id:int}")]
    [RequestTimeout(120_000)] // large PDFs are streamed from Telegram
    public async Task GetFile(int id, [FromQuery] long exp, [FromQuery] string? sig, [FromQuery] bool download = false)
    {
        var ct = HttpContext.RequestAborted;

        if (!_signer.IsValid(id, exp, sig))
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            await Response.WriteAsJsonAsync(new { error = "Ссылка недействительна или устарела. Обновите страницу." }, ct);
            return;
        }

        var attachment = await _db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (attachment == null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // Files never change, so the browser may cache them for the lifetime of the link
        var etag = $"\"att-{attachment.Id}\"";
        if (Request.Headers.IfNoneMatch == etag)
        {
            Response.StatusCode = StatusCodes.Status304NotModified;
            return;
        }

        Response.ContentType = attachment.ContentType;
        Response.Headers.CacheControl = "private, max-age=21600";
        Response.Headers.ETag = etag;
        Response.Headers.ContentDisposition = new System.Net.Mime.ContentDisposition
        {
            FileName = attachment.FileName,
            Inline = !download
        }.ToString();

        try
        {
            await _storage.DownloadAsync(attachment.TelegramFileId, Response.Body, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Не удалось получить файл #{Id} из Telegram", id);
            if (!Response.HasStarted) Response.StatusCode = StatusCodes.Status502BadGateway;
        }
    }
}
