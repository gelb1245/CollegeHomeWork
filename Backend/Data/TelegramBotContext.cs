using Microsoft.EntityFrameworkCore;
using TelegramBotApi.Models;

namespace TelegramBotApi.Data
{
    public class TelegramBotContext : DbContext
    {
        public TelegramBotContext(DbContextOptions<TelegramBotContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<ChatGroup> ChatGroups => Set<ChatGroup>();
        public DbSet<Message> Messages => Set<Message>();
    }
}
