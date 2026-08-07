using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comprexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPreparedPromptCatalogSegments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PreparedClientToolSchemaTokensEstimated",
                table: "ConversationTurnMetrics",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PreparedRulesTokensEstimated",
                table: "ConversationTurnMetrics",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PreparedVirtualToolSchemaTokensEstimated",
                table: "ConversationTurnMetrics",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreparedClientToolSchemaTokensEstimated",
                table: "ConversationTurnMetrics");

            migrationBuilder.DropColumn(
                name: "PreparedRulesTokensEstimated",
                table: "ConversationTurnMetrics");

            migrationBuilder.DropColumn(
                name: "PreparedVirtualToolSchemaTokensEstimated",
                table: "ConversationTurnMetrics");
        }
    }
}
