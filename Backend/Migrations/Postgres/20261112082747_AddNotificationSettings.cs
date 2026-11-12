using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace collegehomework.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddNotificationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MutedSubjects",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    ChatGroupId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MutedSubjects", x => new { x.UserId, x.ChatGroupId });
                    table.ForeignKey(
                        name: "FK_MutedSubjects_ChatGroups_ChatGroupId",
                        column: x => x.ChatGroupId,
                        principalTable: "ChatGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MutedSubjects_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "TelegramId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationSettings",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    NewTasks = table.Column<bool>(type: "boolean", nullable: false),
                    AutoDelete = table.Column<bool>(type: "boolean", nullable: false),
                    QuietHoursEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    QuietFromMinutes = table.Column<int>(type: "integer", nullable: false),
                    QuietToMinutes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationSettings", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_NotificationSettings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "TelegramId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MutedSubjects_ChatGroupId",
                table: "MutedSubjects",
                column: "ChatGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MutedSubjects");

            migrationBuilder.DropTable(
                name: "NotificationSettings");
        }
    }
}
