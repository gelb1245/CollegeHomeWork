using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TelegramBotApi.Attributes;
using TelegramBotApi.Data;
using TelegramBotApi.Models;
using TelegramBotApi.Services;

namespace TelegramBotApi.Controllers;

public record GroupResponseDto(int Id, string Name, string? Description, int MessageCount);

public record CreateGroupDto(
    [Required(ErrorMessage = "Имя группы обязательно")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Длина имени должна быть от 1 до 100 символов")]
    string Name,
    [StringLength(500, ErrorMessage = "Описание не должно превышать 500 символов")]
    string? Description);

public record CreateUserDto(
    [Range(1, long.MaxValue, ErrorMessage = "Некорректный Telegram ID")]
    long TelegramId,
    [StringLength(100, ErrorMessage = "Имя пользователя слишком длинное")]
    string? Username,
    [StringLength(150, ErrorMessage = "Полное имя слишком длинное")]
    string? FullName,
    string Role);

[ApiController]
[Route("api/admin")]
[EnableRateLimiting("strict-api")]
[TelegramAuthorize] // Enforces Telegram WebApp cryptographically-signed authentication for ALL endpoints
public class AdminController : ControllerBase
{
    private readonly TelegramBotContext _db;
    private readonly WhitelistService _whitelist;
    private readonly ILogger<AdminController> _log;

    private User CurrentUser => (User)HttpContext.Items["CurrentUser"]!;

    public AdminController(
        TelegramBotContext db,
        WhitelistService whitelist,
        ILogger<AdminController> log)
    {
        _db = db;
        _whitelist = whitelist;
        _log = log;
    }

    // ---------- User Info / Authentication for Mini App ----------
    [HttpGet("me")]
    public IActionResult GetCurrentAuthUser()
    {
        return Ok(CurrentUser);
    }

    [HttpGet("me/{telegramId}")]
    public async Task<IActionResult> GetCurrentUserById(long telegramId, CancellationToken cancellationToken)
    {
        if (telegramId <= 0)
        {
            return BadRequest(new { error = "Некорректный Telegram ID" });
        }

        // Students can only query their own user info
        if (CurrentUser.Role == RoleType.User && CurrentUser.TelegramId != telegramId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Доступ к информации о другом пользователе запрещен." });
        }

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TelegramId == telegramId, cancellationToken);
        if (user == null)
        {
            if (_whitelist.Contains(telegramId))
            {
                user = new User
                {
                    TelegramId = telegramId,
                    Username = "Пользователь",
                    Role = telegramId == WhitelistService.PrimaryOwnerId ? RoleType.Developer : RoleType.User
                };
                _db.Users.Add(user);
                await _db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                return NotFound(new { error = "Пользователь не найден в вайтлисте" });
            }
        }
        return Ok(user);
    }

    // ---------- Groups / Subjects ----------
    [HttpGet("groups")]
    public async Task<ActionResult<IEnumerable<GroupResponseDto>>> GetAllGroups(CancellationToken cancellationToken)
    {
        var groups = await _db.ChatGroups.AsNoTracking().OrderBy(g => g.Id).ToListAsync(cancellationToken);

        // Badge count: tasks in the subject that the current user has not marked as completed yet
        var userId = CurrentUser.TelegramId;
        var pendingByGroup = await _db.Messages.AsNoTracking()
            .Where(m => !_db.UserTaskProgresses.Any(p => p.MessageId == m.Id && p.UserId == userId && p.IsCompleted))
            .GroupBy(m => m.ChatGroupId)
            .Select(g => new { GroupId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GroupId, x => x.Count, cancellationToken);

        return Ok(groups.Select(g => new GroupResponseDto(
            g.Id, g.Name, g.Description, pendingByGroup.GetValueOrDefault(g.Id))));
    }

    [HttpPost("group")]
    [TelegramAuthorize(RoleType.Developer)] // Only Developer can create new subject groups
    public async Task<ActionResult<ChatGroup>> CreateGroup([FromBody] CreateGroupDto dto, CancellationToken cancellationToken)
    {
        var trimmedName = dto.Name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return BadRequest("Имя группы обязательно.");
        }

        if (trimmedName.Length > 100)
        {
            return BadRequest("Имя группы не должно превышать 100 символов.");
        }

        var group = new ChatGroup
        {
            Name = trimmedName,
            Description = dto.Description?.Trim()
        };

        _db.ChatGroups.Add(group);
        await _db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetGroup), new { id = group.Id }, group);
    }

    [HttpGet("group/{id}")]
    public async Task<ActionResult<ChatGroup>> GetGroup(int id, CancellationToken cancellationToken)
    {
        if (id <= 0) return BadRequest("Некорректный ID группы.");
        var group = await _db.ChatGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        return group == null ? NotFound() : Ok(group);
    }

    [HttpDelete("group/{id}")]
    [TelegramAuthorize(RoleType.Developer)] // Only Developer can delete subjects
    public async Task<IActionResult> DeleteGroup(int id, CancellationToken cancellationToken)
    {
        if (id <= 0) return BadRequest("Некорректный ID группы.");

        var group = await _db.ChatGroups.FindAsync(new object[] { id }, cancellationToken);
        if (group == null) return NotFound();

        var messages = _db.Messages.Where(m => m.ChatGroupId == id);
        _db.Messages.RemoveRange(messages);

        _db.ChatGroups.Remove(group);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // ---------- Users / Whitelist Management ----------
    [HttpGet("users")]
    [TelegramAuthorize(RoleType.Admin, RoleType.Developer)] // Only Admins and Developers can view user list
    public async Task<ActionResult<IEnumerable<User>>> GetUsers(CancellationToken cancellationToken)
    {
        return Ok(await _db.Users.AsNoTracking().OrderBy(u => u.Role).ToListAsync(cancellationToken));
    }

    [HttpPost("user")]
    [TelegramAuthorize(RoleType.Developer)] // Only Developer can add users to whitelist
    public async Task<IActionResult> AddUser([FromBody] CreateUserDto dto, CancellationToken cancellationToken)
    {
        if (dto.TelegramId <= 0) return BadRequest("Некорректный Telegram ID");

        if (!Enum.TryParse<RoleType>(dto.Role, true, out var role))
            role = RoleType.User;

        _whitelist.Add(dto.TelegramId);

        var existing = await _db.Users.FindAsync(new object[] { dto.TelegramId }, cancellationToken);
        if (existing == null)
        {
            existing = new User
            {
                TelegramId = dto.TelegramId,
                Username = dto.Username?.Trim() ?? $"User_{dto.TelegramId}",
                FullName = dto.FullName?.Trim() ?? dto.Username?.Trim(),
                Role = role
            };
            _db.Users.Add(existing);
        }
        else
        {
            existing.Role = role;
            if (!string.IsNullOrWhiteSpace(dto.Username)) existing.Username = dto.Username.Trim();
            if (!string.IsNullOrWhiteSpace(dto.FullName)) existing.FullName = dto.FullName.Trim();
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(existing);
    }

    [HttpPatch("user/{telegramId}/role/{role}")]
    [TelegramAuthorize(RoleType.Developer)] // Only Developer can change roles
    public async Task<IActionResult> SetUserRole(long telegramId, string role, CancellationToken cancellationToken)
    {
        if (telegramId <= 0) return BadRequest("Некорректный Telegram ID");

        var user = await _db.Users.FindAsync(new object[] { telegramId }, cancellationToken);
        if (user == null) return NotFound("Пользователь не найден");

        if (!Enum.TryParse<RoleType>(role, true, out var newRole))
            return BadRequest("Некорректная роль. Допустимые: Developer, Admin, User");

        // Primary owner cannot be demoted
        if (telegramId == WhitelistService.PrimaryOwnerId && newRole != RoleType.Developer)
            return BadRequest("Нельзя понизить роль главного владельца бота.");

        user.Role = newRole;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = $"Роль пользователя {telegramId} изменена на {newRole}" });
    }

    [HttpDelete("user/{telegramId}")]
    [TelegramAuthorize(RoleType.Developer)] // Only Developer can remove users
    public async Task<IActionResult> DeleteUser(long telegramId, CancellationToken cancellationToken)
    {
        if (telegramId <= 0) return BadRequest("Некорректный Telegram ID");

        if (telegramId == WhitelistService.PrimaryOwnerId)
        {
            return BadRequest("Нельзя удалить главного создателя бота.");
        }

        _whitelist.Remove(telegramId);

        var user = await _db.Users.FindAsync(new object[] { telegramId }, cancellationToken);
        if (user != null)
        {
            _db.Users.Remove(user);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Ok(new { message = $"Пользователь {telegramId} удален из вайтлиста и системы." });
    }
}
