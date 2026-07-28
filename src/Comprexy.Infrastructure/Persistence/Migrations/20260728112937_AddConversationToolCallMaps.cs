using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comprexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationToolCallMaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConversationToolCallMaps",
                columns: table => new
                {
                    ClusterId = table.Column<long>(type: "INTEGER", nullable: false),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IrCallId = table.Column<string>(type: "TEXT", nullable: false),
                    ClientCallId = table.Column<string>(type: "TEXT", nullable: false),
                    ComprexyToolName = table.Column<string>(type: "TEXT", nullable: false),
                    ClientToolName = table.Column<string>(type: "TEXT", nullable: true),
                    IrArgumentsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ClientArgumentsJson = table.Column<string>(type: "TEXT", nullable: true),
                    Strategy = table.Column<string>(type: "TEXT", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: true),
                    StartLine = table.Column<int>(type: "INTEGER", nullable: true),
                    EndLine = table.Column<int>(type: "INTEGER", nullable: true),
                    Pending = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    RegisteredAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationToolCallMaps", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationToolCallMaps_ClusterId",
                table: "ConversationToolCallMaps",
                column: "ClusterId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationToolCallMaps_ConversationId_ClientCallId",
                table: "ConversationToolCallMaps",
                columns: new[] { "ConversationId", "ClientCallId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationToolCallMaps_ConversationId_IrCallId",
                table: "ConversationToolCallMaps",
                columns: new[] { "ConversationId", "IrCallId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationToolCallMaps_ConversationId_Pending_RegisteredAt",
                table: "ConversationToolCallMaps",
                columns: new[] { "ConversationId", "Pending", "RegisteredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationToolCallMaps");
        }
    }
}
