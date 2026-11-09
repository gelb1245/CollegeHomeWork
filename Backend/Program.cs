using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Telegram.Bot;
using TelegramBotApi.Data;
using TelegramBotApi.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Configure Kestrel Limits (Anti-DDoS, Slowloris protection, Resource limits)
builder.WebHost.ConfigureKestrel(options =>
{
    // Allow up to 25 MB request body size for photos, PDF documents and homework attachments
    options.Limits.MaxRequestBodySize = 25 * 1024 * 1024;
    // Limit request header size
    options.Limits.MaxRequestHeadersTotalSize = 32 * 1024;
    // Slowloris header read timeout
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
    // Connection keep-alive
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(1);
    // Drop slow-trickle connections (Slowloris / slow POST DoS)
    options.Limits.MinRequestBodyDataRate = new MinDataRate(bytesPerSecond: 240, gracePeriod: TimeSpan.FromSeconds(5));
    options.Limits.MinResponseDataRate = new MinDataRate(bytesPerSecond: 240, gracePeriod: TimeSpan.FromSeconds(5));
    // Limit concurrent open connections per server instance
    options.Limits.MaxConcurrentConnections = 250;
    // Do not disclose server technology in response headers
    options.AddServerHeader = false;
});

// 2. Configure Forwarded Headers for reverse proxies (Render, Docker, Nginx, Cloudflare)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// 3. Database with connection pooling and busy timeout
builder.Services.AddDbContext<TelegramBotContext>(opt =>
    opt.UseSqlite("Data Source=telegrambot.db;Cache=Shared;Mode=ReadWriteCreate;Default Timeout=15"));

// 4. Rate Limiting Protection (ASP.NET Core 8)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        var retryAfter = "60";
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retrySpan))
        {
            retryAfter = Math.Max(1, (int)retrySpan.TotalSeconds).ToString();
        }
        context.HttpContext.Response.Headers.RetryAfter = retryAfter;
        await context.HttpContext.Response.WriteAsync(
            $"{{\"error\":\"Слишком много запросов. Пожалуйста, подождите {retryAfter} сек.\",\"retryAfter\":{retryAfter}}}",
            cancellationToken: token);
    };

    // Global IP-based rate limiter (Sliding Window: 100 requests per 1 minute per IP)
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: clientIp,
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0
            });
    });

    // Strict policy for API endpoints & mutations: 30 requests per 1 minute per IP
    options.AddPolicy("strict-api", httpContext =>
    {
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: clientIp,
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0
            });
    });
});

// 5. Request Timeouts to abort hanging requests
builder.Services.AddRequestTimeouts(options =>
{
    options.DefaultPolicy = new RequestTimeoutPolicy
    {
        Timeout = TimeSpan.FromSeconds(30)
    };
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 6. CORS Policy
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .WithMethods("GET", "POST", "PATCH", "DELETE", "OPTIONS");
    });
});

// 7. Security & Business Services
builder.Services.AddSingleton<TelegramRateLimiter>();
builder.Services.AddSingleton<WhitelistService>();
builder.Services.AddSingleton<TelegramAuthService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN") 
                ?? config["Telegram:BotToken"] 
                ?? "8588883105:AAF4PXCekltVPEYaGT2W5bIdeaAAMOFPbTE";
    var logger = sp.GetRequiredService<ILogger<TelegramAuthService>>();
    return new TelegramAuthService(token, logger);
});
builder.Services.AddSingleton<HomeworkNotificationService>();

// 8. Configure Telegram Bot Client with user's token
builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN") 
                ?? builder.Configuration["Telegram:BotToken"];
    if (string.IsNullOrWhiteSpace(token) || token == "YOUR_BOT_TOKEN_HERE")
    {
        token = "8588883105:AAF4PXCekltVPEYaGT2W5bIdeaAAMOFPbTE";
    }
    return new TelegramBotClient(token);
});

// Register Background Services (Telegram Polling & 24-hour Notification Auto-Deleter)
builder.Services.AddHostedService<TelegramBotPollingService>();
builder.Services.AddHostedService<NotificationCleanupService>();

var app = builder.Build();

// Enable Forwarded Headers (MUST be early in pipeline)
app.UseForwardedHeaders();

// Global Exception Handler to prevent info leakage
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"error\":\"Внутренняя ошибка сервера. Повторите запрос позже.\"}");
    });
});

// Security Headers Middleware
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    // Allow embedding in Telegram WebApp while preventing clickjacking from other domains
    context.Response.Headers.Append("Content-Security-Policy",
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://telegram.org; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com data:; " +
        "img-src 'self' data: https:; " +
        "connect-src 'self' https:; " +
        "frame-ancestors 'self' https://web.telegram.org https://*.telegram.org;");
    context.Response.Headers.Remove("Server");
    context.Response.Headers.Remove("X-Powered-By");
    await next();
});

// Apply Rate Limiting
app.UseRateLimiter();

// Apply Request Timeouts
app.UseRequestTimeouts();

// Swagger Documentation (Enabled in Dev or if explicitly enabled)
if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("EnableSwagger", false))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "College Homework API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseCors();

// Serve static frontend files (HTML, CSS, JS) safely
var possiblePaths = new[]
{
    Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "Frontend")),
    Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "Frontend")),
    "/Frontend"
};

var frontendPath = possiblePaths.FirstOrDefault(Directory.Exists);
if (frontendPath != null)
{
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = new PhysicalFileProvider(frontendPath),
        RequestPath = ""
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(frontendPath),
        RequestPath = ""
    });
}

app.UseAuthorization();
app.MapControllers();

// Initialize SQLite with WAL mode & busy timeout to prevent database locked errors under load
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TelegramBotContext>();
    await db.Database.EnsureCreatedAsync();
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;");
    await db.Database.ExecuteSqlRawAsync("PRAGMA synchronous = NORMAL;");
    await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout = 5000;");

    // Ensure Deadline column exists on Messages
    try
    {
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE Messages ADD COLUMN Deadline TEXT NULL;");
    }
    catch { /* Column already exists */ }

    // Ensure UserTaskProgresses table exists
    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS UserTaskProgresses (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            UserId INTEGER NOT NULL,
            MessageId INTEGER NOT NULL,
            IsCompleted INTEGER NOT NULL DEFAULT 0,
            CompletedAt TEXT NULL,
            CONSTRAINT FK_UserTaskProgresses_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (TelegramId) ON DELETE CASCADE,
            CONSTRAINT FK_UserTaskProgresses_Messages_MessageId FOREIGN KEY (MessageId) REFERENCES Messages (Id) ON DELETE CASCADE
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_UserTaskProgresses_UserId_MessageId ON UserTaskProgresses (UserId, MessageId);

        CREATE TABLE IF NOT EXISTS SentNotifications (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            TelegramChatId INTEGER NOT NULL,
            TelegramMessageId INTEGER NOT NULL,
            HomeworkMessageId INTEGER NOT NULL,
            SentAt TEXT NOT NULL,
            DeleteAt TEXT NOT NULL,
            IsDeleted INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS IX_SentNotifications_IsDeleted_DeleteAt ON SentNotifications (IsDeleted, DeleteAt);
        CREATE INDEX IF NOT EXISTS IX_SentNotifications_HomeworkMessageId ON SentNotifications (HomeworkMessageId);
    ");
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogWarning(ex, "Не удалось настроить PRAGMA или схему для SQLite");
}

app.Run();
