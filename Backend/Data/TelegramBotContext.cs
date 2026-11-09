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
        public DbSet<UserTaskProgress> UserTaskProgresses => Set<UserTaskProgress>();
        public DbSet<SentNotification> SentNotifications => Set<SentNotification>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<UserTaskProgress>()
                .HasIndex(p => new { p.UserId, p.MessageId })
                .IsUnique();

            modelBuilder.Entity<SentNotification>()
                .HasIndex(n => new { n.IsDeleted, n.DeleteAt });

            modelBuilder.Entity<SentNotification>()
                .HasIndex(n => n.HomeworkMessageId);
        }
    }
}
