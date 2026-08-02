using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comprexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTurnMetricDurations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DurationMs",
                table: "ConversationTurnMetrics",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrepareDurationMs",
                table: "ConversationTurnMetrics",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpstreamDurationMs",
                table: "ConversationTurnMetrics",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DurationMs",
                table: "ConversationTurnMetrics");

            migrationBuilder.DropColumn(
                name: "PrepareDurationMs",
                table: "ConversationTurnMetrics");

            migrationBuilder.DropColumn(
                name: "UpstreamDurationMs",
                table: "ConversationTurnMetrics");
        }
    }
}
