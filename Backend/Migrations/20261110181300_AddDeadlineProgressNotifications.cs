using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace collegehomework.Migrations
{
    /// <inheritdoc />
    public partial class AddDeadlineProgressNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "Deadline",
                table: "Messages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SentNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TelegramChatId = table.Column<long>(type: "INTEGER", nullable: false),
                    TelegramMessageId = table.Column<int>(type: "INTEGER", nullable: false),
                    HomeworkMessageId = table.Column<int>(type: "INTEGER", nullable: false),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeleteAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserTaskProgresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    MessageId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTaskProgresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserTaskProgresses_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserTaskProgresses_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "TelegramId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SentNotifications_HomeworkMessageId",
                table: "SentNotifications",
                column: "HomeworkMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_SentNotifications_IsDeleted_DeleteAt",
                table: "SentNotifications",
                columns: new[] { "IsDeleted", "DeleteAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserTaskProgresses_MessageId",
                table: "UserTaskProgresses",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_UserTaskProgresses_UserId_MessageId",
                table: "UserTaskProgresses",
                columns: new[] { "UserId", "MessageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SentNotifications");

            migrationBuilder.DropTable(
                name: "UserTaskProgresses");

            migrationBuilder.DropColumn(
                name: "Deadline",
                table: "Messages");
        }
    }
}
