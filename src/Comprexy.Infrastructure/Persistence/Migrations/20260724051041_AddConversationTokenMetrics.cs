using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comprexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationTokenMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CompletionTokens",
                table: "CompressionEvents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PromptTokens",
                table: "CompressionEvents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TokensAreEstimated",
                table: "CompressionEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TotalTokens",
                table: "CompressionEvents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConversationMetricsSummaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TotalTurns = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalRawInputTokensEstimated = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalCompressedPromptTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalCompletionTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalCompressionOverheadTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalBaselineTokensEstimated = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalActualTokensEstimated = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalNetTokensSaved = table.Column<long>(type: "INTEGER", nullable: false),
                    AverageTokenSavingsRatio = table.Column<double>(type: "REAL", nullable: false),
                    CompressionEventCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ClusterId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationMetricsSummaries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConversationTurnMetrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TurnIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestStartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    RawInputTokensEstimated = table.Column<int>(type: "INTEGER", nullable: false),
                    CompressedInputTokensEstimated = table.Column<int>(type: "INTEGER", nullable: false),
                    ActualPromptTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    ActualCompletionTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    BaselineTotalTokensEstimated = table.Column<int>(type: "INTEGER", nullable: false),
                    CompressedTotalTokensEstimated = table.Column<int>(type: "INTEGER", nullable: false),
                    NetTokensSaved = table.Column<int>(type: "INTEGER", nullable: false),
                    NetTokenSavingsRatio = table.Column<double>(type: "REAL", nullable: false),
                    SoftBudgetExceeded = table.Column<bool>(type: "INTEGER", nullable: false),
                    HardBudgetExceeded = table.Column<bool>(type: "INTEGER", nullable: false),
                    TrimTriggered = table.Column<bool>(type: "INTEGER", nullable: false),
                    WorkingMemoryVersionUsed = table.Column<int>(type: "INTEGER", nullable: true),
                    RawMessageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SentMessageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SentPayloadHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ClusterId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationTurnMetrics", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMetricsSummaries_ClusterId",
                table: "ConversationMetricsSummaries",
                column: "ClusterId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMetricsSummaries_ConversationId",
                table: "ConversationMetricsSummaries",
                column: "ConversationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMetricsSummaries_UpdatedAt",
                table: "ConversationMetricsSummaries",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTurnMetrics_ClusterId",
                table: "ConversationTurnMetrics",
                column: "ClusterId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTurnMetrics_ConversationId_CreatedAt",
                table: "ConversationTurnMetrics",
                columns: new[] { "ConversationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTurnMetrics_ConversationId_TurnIndex",
                table: "ConversationTurnMetrics",
                columns: new[] { "ConversationId", "TurnIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationMetricsSummaries");

            migrationBuilder.DropTable(
                name: "ConversationTurnMetrics");

            migrationBuilder.DropColumn(
                name: "CompletionTokens",
                table: "CompressionEvents");

            migrationBuilder.DropColumn(
                name: "PromptTokens",
                table: "CompressionEvents");

            migrationBuilder.DropColumn(
                name: "TokensAreEstimated",
                table: "CompressionEvents");

            migrationBuilder.DropColumn(
                name: "TotalTokens",
                table: "CompressionEvents");
        }
    }
}
