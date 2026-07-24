using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comprexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddToolSchemaCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPinnedForToolSchema",
                table: "ConversationMessages",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ConversationToolCatalogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CatalogHash = table.Column<string>(type: "TEXT", nullable: false),
                    CompactIndexJson = table.Column<string>(type: "TEXT", nullable: false),
                    SnapshottedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ClusterId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationToolCatalogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConversationToolDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToolName = table.Column<string>(type: "TEXT", nullable: false),
                    DefinitionHash = table.Column<string>(type: "TEXT", nullable: false),
                    DefinitionJson = table.Column<string>(type: "TEXT", nullable: false),
                    HydratedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ClusterId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationToolDefinitions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationToolCatalogs_ClusterId",
                table: "ConversationToolCatalogs",
                column: "ClusterId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationToolCatalogs_ConversationId",
                table: "ConversationToolCatalogs",
                column: "ConversationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationToolDefinitions_ClusterId",
                table: "ConversationToolDefinitions",
                column: "ClusterId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationToolDefinitions_ConversationId",
                table: "ConversationToolDefinitions",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationToolDefinitions_ConversationId_ToolName",
                table: "ConversationToolDefinitions",
                columns: new[] { "ConversationId", "ToolName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationToolCatalogs");

            migrationBuilder.DropTable(
                name: "ConversationToolDefinitions");

            migrationBuilder.DropColumn(
                name: "IsPinnedForToolSchema",
                table: "ConversationMessages");
        }
    }
}
