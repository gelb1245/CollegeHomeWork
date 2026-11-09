using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TelegramBotApi.Data;

namespace TelegramBotApi;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TelegramBotContext>
{
    public TelegramBotContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TelegramBotContext>();
        optionsBuilder.UseSqlite("Data Source=telegrambot.db");
        return new TelegramBotContext(optionsBuilder.Options);
    }
}
