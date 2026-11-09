using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Telegram.Bot;
using TelegramBotApi.Data;
using TelegramBotApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Add Database
builder.Services.AddDbContext<TelegramBotContext>(opt =>
    opt.UseSqlite("Data Source=telegrambot.db"));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Add Whitelist Service
builder.Services.AddSingleton<WhitelistService>();

// Configure Telegram Bot Client with user's token
builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var token = builder.Configuration["Telegram:BotToken"];
    if (string.IsNullOrWhiteSpace(token) || token == "YOUR_BOT_TOKEN_HERE")
    {
        return new TelegramBotClient("8588883105:AAF4PXCekltVPEYaGT2W5bIdeaAAMOFPbTE");
    }
    return new TelegramBotClient(token);
});

// Register Long Polling Background Service for immediate Telegram bot activity
builder.Services.AddHostedService<TelegramBotPollingService>();

var app = builder.Build();

// Swagger Documentation
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "College Homework API v1");
    c.RoutePrefix = "swagger";
});

app.UseCors();

// Serve static frontend files (HTML, CSS, JS) from Frontend directory
var frontendPath = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "Frontend"));
if (Directory.Exists(frontendPath))
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

app.Run();
