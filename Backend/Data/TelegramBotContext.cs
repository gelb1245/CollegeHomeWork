using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TelegramBotApi.Models;

namespace TelegramBotApi.Data
{
    public class TelegramBotContext : DbContext
    {
        public TelegramBotContext(DbContextOptions<TelegramBotContext> options)
            : base(options)
        {
        }

        // Used by provider-specific subclasses (see PostgresTelegramBotContext)
        protected TelegramBotContext(DbContextOptions options)
            : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<ChatGroup> ChatGroups => Set<ChatGroup>();
        public DbSet<Message> Messages => Set<Message>();
        public DbSet<UserTaskProgress> UserTaskProgresses => Set<UserTaskProgress>();
        public DbSet<SentNotification> SentNotifications => Set<SentNotification>();
        public DbSet<Attachment> Attachments => Set<Attachment>();

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

            // All times are stored in UTC, but SQLite/PostgreSQL ("timestamp without time zone") return them
            // with DateTimeKind.Unspecified. Then JSON has no "Z" and the browser treats UTC as local time
            // (deadlines were shown 2 hours early in Kyiv). Mark every DateTime read from the DB as UTC.
            var utc = new ValueConverter<DateTime, DateTime>(
                v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            var utcNullable = new ValueConverter<DateTime?, DateTime?>(
                v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
            {
                if (property.ClrType == typeof(DateTime)) property.SetValueConverter(utc);
                else if (property.ClrType == typeof(DateTime?)) property.SetValueConverter(utcNullable);
            }
        }
    }
}
