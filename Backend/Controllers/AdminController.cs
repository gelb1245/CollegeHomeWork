using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TelegramBotApi.Data;
using TelegramBotApi.Models;
using TelegramBotApi.Services;

namespace TelegramBotApi.Controllers;

public record MessageResponseDto(int Id, int ChatGroupId, long FromUserId, string Author, string Text, DateTime SentAt);
public record CreateGroupDto(string Name, string? Description);
public record CreateMessageDto(string Text, long? FromUserId);
public record CreateUserDto(long TelegramId, string? Username, string? FullName, string Role);

[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly TelegramBotContext _db;
    private readonly WhitelistService _whitelist;
    private readonly ILogger<AdminController> _log;

    public AdminController(TelegramBotContext db, WhitelistService whitelist, ILogger<AdminController> log)
    {
        _db = db;
        _whitelist = whitelist;
        _log = log;
    }

    // ---------- User Info / Authentication for Mini App ----------
    [HttpGet("me/{telegramId}")]
    public async Task<IActionResult> GetCurrentUser(long telegramId)
    {
        var user = await _db.Users.FindAsync(telegramId);
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
                await _db.SaveChangesAsync();
            }
            else
            {
                return NotFound(new { error = "Пользователь не найден в вайтлисте" });
            }
        }
        return Ok(user);
    }

    // ---------- Groups ----------
    [HttpGet("groups")]
    public async Task<ActionResult<IEnumerable<ChatGroup>>> GetAllGroups()
    {
        var groups = await _db.ChatGroups.ToListAsync();
        if (groups.Count == 0)
        {
            var defaultGroups = new List<ChatGroup>
            {
                new() { Name = "История", Description = "Лекции и домашние задания по Истории" },
                new() { Name = "Всесвит", Description = "Всемирная история и география" },
                new() { Name = "ДЗ Общее", Description = "Срочные задания и дедлайны по всем предметам" }
            };
            _db.ChatGroups.AddRange(defaultGroups);
            await _db.SaveChangesAsync();
            groups = defaultGroups;
        }
        return Ok(groups);
    }

    [HttpPost("group")]
    public async Task<ActionResult<ChatGroup>> CreateGroup([FromBody] CreateGroupDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return BadRequest("Имя группы обязательно.");
        }

        var group = new ChatGroup
        {
            Name = dto.Name.Trim(),
            Description = dto.Description
        };

        _db.ChatGroups.Add(group);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetGroup), new { id = group.Id }, group);
    }

    [HttpGet("group/{id}")]
    public async Task<ActionResult<ChatGroup>> GetGroup(int id)
    {
        var group = await _db.ChatGroups.FindAsync(id);
        return group == null ? NotFound() : Ok(group);
    }

    [HttpDelete("group/{id}")]
    public async Task<IActionResult> DeleteGroup(int id)
    {
        var group = await _db.ChatGroups.FindAsync(id);
        if (group == null) return NotFound();

        var messages = _db.Messages.Where(m => m.ChatGroupId == id);
        _db.Messages.RemoveRange(messages);

        _db.ChatGroups.Remove(group);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- Users / Whitelist Management ----------
    [HttpGet("users")]
    public async Task<ActionResult<IEnumerable<User>>> GetUsers()
    {
        return Ok(await _db.Users.OrderBy(u => u.Role).ToListAsync());
    }

    [HttpPost("user")]
    public async Task<IActionResult> AddUser([FromBody] CreateUserDto dto)
    {
        if (dto.TelegramId <= 0) return BadRequest("Некорректный Telegram ID");

        if (!Enum.TryParse<RoleType>(dto.Role, true, out var role))
            role = RoleType.User;

        _whitelist.Add(dto.TelegramId);

        var existing = await _db.Users.FindAsync(dto.TelegramId);
        if (existing == null)
        {
            existing = new User
            {
                TelegramId = dto.TelegramId,
                Username = dto.Username ?? $"User_{dto.TelegramId}",
                FullName = dto.FullName ?? dto.Username,
                Role = role
            };
            _db.Users.Add(existing);
        }
        else
        {
            existing.Role = role;
            if (!string.IsNullOrEmpty(dto.Username)) existing.Username = dto.Username;
            if (!string.IsNullOrEmpty(dto.FullName)) existing.FullName = dto.FullName;
        }

        await _db.SaveChangesAsync();
        return Ok(existing);
    }

    [HttpPatch("user/{telegramId}/role/{role}")]
    public async Task<IActionResult> SetUserRole(long telegramId, string role)
    {
        var user = await _db.Users.FindAsync(telegramId);
        if (user == null) return NotFound("Пользователь не найден");

        if (!Enum.TryParse<RoleType>(role, true, out var newRole))
            return BadRequest("Некорректная роль. Допустимые: Developer, Admin, User");

        // Primary owner cannot be demoted
        if (telegramId == WhitelistService.PrimaryOwnerId && newRole != RoleType.Developer)
            return BadRequest("Нельзя понизить роль главного владельца бота.");

        user.Role = newRole;
        await _db.SaveChangesAsync();
        return Ok(new { message = $"Роль пользователя {telegramId} изменена на {newRole}" });
    }

    [HttpDelete("user/{telegramId}")]
    public async Task<IActionResult> DeleteUser(long telegramId)
    {
        if (telegramId == WhitelistService.PrimaryOwnerId)
        {
            return BadRequest("Нельзя удалить главного создателя бота.");
        }

        _whitelist.Remove(telegramId);

        var user = await _db.Users.FindAsync(telegramId);
        if (user != null)
        {
            _db.Users.Remove(user);
            await _db.SaveChangesAsync();
        }

        return Ok(new { message = $"Пользователь {telegramId} удален из вайтлиста и системы." });
    }

    // ---------- Messages ----------
    [HttpGet("group/{groupId}/messages")]
    public async Task<ActionResult<IEnumerable<MessageResponseDto>>> GetMessages(int groupId)
    {
        var msgs = await _db.Messages
            .Where(m => m.ChatGroupId == groupId)
            .OrderBy(m => m.SentAt)
            .Select(m => new MessageResponseDto(
                m.Id,
                m.ChatGroupId,
                m.FromUserId,
                m.FromUser != null ? (m.FromUser.FullName ?? m.FromUser.Username) : "Администратор",
                m.Text,
                m.SentAt
            ))
            .ToListAsync();
        return Ok(msgs);
    }

    [HttpPost("group/{groupId}/message")]
    public async Task<IActionResult> PostMessage(int groupId, [FromBody] CreateMessageDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Text))
            return BadRequest("Текст сообщения пуст");

        var group = await _db.ChatGroups.FindAsync(groupId);
        if (group == null) return NotFound("Группа не найдена");

        var msg = new Message
        {
            ChatGroupId = groupId,
            FromUserId = dto.FromUserId ?? 0,
            Text = dto.Text.Trim(),
            SentAt = DateTime.UtcNow
        };

        _db.Messages.Add(msg);
        await _db.SaveChangesAsync();
        return Ok(msg);
    }

    [HttpDelete("message/{id}")]
    public async Task<IActionResult> DeleteMessage(int id)
    {
        var msg = await _db.Messages.FindAsync(id);
        if (msg == null) return NotFound();

        _db.Messages.Remove(msg);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
