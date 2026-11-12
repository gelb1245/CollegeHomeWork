using Microsoft.AspNetCore.Mvc;
using TelegramBotApi.Attributes;
using TelegramBotApi.Models;
using TelegramBotApi.Services;

namespace TelegramBotApi.Controllers;

/// <summary>Search over all tasks (any role).</summary>
[ApiController]
[Route("api/admin/search")]
[TelegramAuthorize]
public class SearchController : ControllerBase
{
    private readonly SearchService _search;

    public SearchController(SearchService search) => _search = search;

    private User CurrentUser => (User)HttpContext.Items["CurrentUser"]!;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SearchHit>>> Search([FromQuery] string? q, CancellationToken ct)
    {
        if (q is { Length: > 100 }) return BadRequest(new { error = "Слишком длинный запрос." });
        return Ok(await _search.SearchAsync(q, CurrentUser.TelegramId, ct));
    }
}
