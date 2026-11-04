using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RemoteLink.Host.Migrations
{
    /// <inheritdoc />
    public partial class InitialSqlite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RemoteDeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastMessageUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastReadUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatConversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SenderDeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecipientDeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 10000, nullable: false),
                    Direction = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SentUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeliveredUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReadUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessages_ChatConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "ChatConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_RemoteDeviceId",
                table: "ChatConversations",
                column: "RemoteDeviceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ConversationId_CreatedUtc_Id",
                table: "ChatMessages",
                columns: new[] { "ConversationId", "CreatedUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_Direction_Status_CreatedUtc",
                table: "ChatMessages",
                columns: new[] { "Direction", "Status", "CreatedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "ChatConversations");
        }
    }
}
