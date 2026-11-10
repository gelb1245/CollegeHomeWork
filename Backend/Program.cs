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
// Override with env var ConnectionStrings__Default, e.g. "Data Source=/var/data/telegrambot.db"
// when the database lives on a persistent disk (Render, Docker volume).
var connectionString = builder.Configuration.GetConnectionString("Default")
                       ?? "Data Source=telegrambot.db;Cache=Shared;Mode=ReadWriteCreate;Default Timeout=15";
builder.Services.AddDbContext<TelegramBotContext>(opt => opt.UseSqlite(connectionString));

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
// Bot token is read ONLY from the environment / user secrets / non-committed config.
// Never hard-code it: anyone with access to the repository could take over the bot.
var botToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")
               ?? builder.Configuration["Telegram:BotToken"];
if (string.IsNullOrWhiteSpace(botToken) || botToken == "YOUR_BOT_TOKEN_HERE")
{
    throw new InvalidOperationException(
        "Не задан токен Telegram-бота. Укажите переменную окружения TELEGRAM_BOT_TOKEN " +
        "или Telegram:BotToken в appsettings.Development.json / dotnet user-secrets.");
}

builder.Services.AddSingleton<TelegramAuthService>(sp =>
    new TelegramAuthService(botToken, sp.GetRequiredService<ILogger<TelegramAuthService>>()));
builder.Services.AddSingleton<HomeworkNotificationService>();

// 8. Configure Telegram Bot Client
builder.Services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(botToken));

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
        "script-src 'self' 'unsafe-inline' https://telegram.org https://cdn.jsdelivr.net; " +
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

// Apply EF Core migrations (schema is managed ONLY by migrations — see Backend/Migrations).
// If this fails the app must not start with a broken schema, so the exception is not swallowed.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TelegramBotContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await DatabaseInitializer.InitializeAsync(db, logger);
}

app.Run();
