using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using TelegramBotApi.Data;
using TelegramBotApi.Models;
using TelegramBotApi.Services;

namespace TelegramBotApi.Attributes;

/// <summary>
/// Action filter enforcing Telegram WebApp cryptographic authentication, whitelist membership,
/// and role-based access control (RBAC).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class TelegramAuthorizeAttribute : ActionFilterAttribute
{
    private readonly RoleType[] _allowedRoles;

    public TelegramAuthorizeAttribute(params RoleType[] allowedRoles)
    {
        _allowedRoles = allowedRoles;
    }

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var httpContext = context.HttpContext;
        var authService = httpContext.RequestServices.GetRequiredService<TelegramAuthService>();
        var db = httpContext.RequestServices.GetRequiredService<TelegramBotContext>();
        var whitelist = httpContext.RequestServices.GetRequiredService<WhitelistService>();
        var env = httpContext.RequestServices.GetRequiredService<IWebHostEnvironment>();
        var logger = httpContext.RequestServices.GetRequiredService<ILogger<TelegramAuthorizeAttribute>>();

        string? rawInitData = null;
        if (httpContext.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var headerStr = authHeader.ToString();
            if (headerStr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                rawInitData = headerStr["Bearer ".Length..].Trim();
            }
            else
            {
                rawInitData = headerStr.Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(rawInitData) && httpContext.Request.Headers.TryGetValue("X-Telegram-Init-Data", out var customHeader))
        {
            rawInitData = customHeader.ToString().Trim();
        }

        TelegramValidatedUser? validatedUser = null;

        if (!string.IsNullOrWhiteSpace(rawInitData))
        {
            if (!authService.ValidateInitData(rawInitData, out validatedUser, out var error))
            {
                context.Result = new ObjectResult(new { error = error ?? "Недействительная сессия Telegram WebApp." })
                {
                    StatusCode = StatusCodes.Status401Unauthorized
                };
                return;
            }
        }
        else if (env.IsDevelopment())
        {
            // Dev-only fallback for testing outside Telegram (e.g. Swagger / local browser)
            long devId = WhitelistService.PrimaryOwnerId;
            if (httpContext.Request.Headers.TryGetValue("X-Dev-Telegram-Id", out var devIdHeader) &&
                long.TryParse(devIdHeader, out var parsedDevId) && parsedDevId > 0)
            {
                devId = parsedDevId;
            }

            validatedUser = new TelegramValidatedUser(devId, "Developer_Dev", "Dev", "Local");
            logger.LogDebug("Dev-режим: авторизован TelegramId {TelegramId} без initData", devId);
        }
        else
        {
            context.Result = new ObjectResult(new { error = "Требуется авторизация Telegram WebApp (отсутствует заголовок Authorization)." })
            {
                StatusCode = StatusCodes.Status401Unauthorized
            };
            return;
        }

        var telegramId = validatedUser!.TelegramId;

        // 1. Whitelist verification
        if (!whitelist.Contains(telegramId))
        {
            logger.LogWarning("Пользователь {TelegramId} не найден в вайтлисте", telegramId);
            context.Result = new ObjectResult(new
            {
                error = "Доступ запрещен. Вас нет в списке студентов (вайтлисте).",
                telegramId = telegramId
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        // 2. Fetch or create User in SQLite
        var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId, httpContext.RequestAborted);
        if (user == null)
        {
            user = new User
            {
                TelegramId = telegramId,
                Username = validatedUser.Username ?? validatedUser.FirstName ?? $"User_{telegramId}",
                FullName = $"{validatedUser.FirstName} {validatedUser.LastName}".Trim(),
                Role = telegramId == WhitelistService.PrimaryOwnerId ? RoleType.Developer : RoleType.User
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(httpContext.RequestAborted);
        }
        else if (telegramId == WhitelistService.PrimaryOwnerId && user.Role != RoleType.Developer)
        {
            user.Role = RoleType.Developer;
            await db.SaveChangesAsync(httpContext.RequestAborted);
        }

        // 3. Role-based access control (RBAC)
        if (_allowedRoles.Length > 0 && !_allowedRoles.Contains(user.Role))
        {
            logger.LogWarning("Пользователю {TelegramId} с ролью {Role} отказано в доступе (требуется: {RequiredRoles})",
                telegramId, user.Role, string.Join(", ", _allowedRoles));

            context.Result = new ObjectResult(new
            {
                error = $"Недостаточно прав. Требуется роль: {string.Join(" или ", _allowedRoles)}, ваша роль: {user.Role}"
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        httpContext.Items["CurrentUser"] = user;
        await next();
    }
}
