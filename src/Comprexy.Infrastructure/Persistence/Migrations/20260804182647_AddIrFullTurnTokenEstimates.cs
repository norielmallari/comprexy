using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comprexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIrFullTurnTokenEstimates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IrFullInputTokensEstimated",
                table: "ConversationTurnMetrics",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VirtualToolsTokensSaved",
                table: "ConversationTurnMetrics",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TotalVirtualToolsTokensSaved",
                table: "ConversationMetricsSummaries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IrFullInputTokensEstimated",
                table: "ConversationTurnMetrics");

            migrationBuilder.DropColumn(
                name: "VirtualToolsTokensSaved",
                table: "ConversationTurnMetrics");

            migrationBuilder.DropColumn(
                name: "TotalVirtualToolsTokensSaved",
                table: "ConversationMetricsSummaries");
        }
    }
}
