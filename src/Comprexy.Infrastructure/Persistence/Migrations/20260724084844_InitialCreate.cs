using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comprexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompressionEvents",
                columns: table => new
                {
                    ClusterId = table.Column<long>(type: "INTEGER", nullable: false),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    OriginalTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    CompressedTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    WorkingMemoryVersionBefore = table.Column<int>(type: "INTEGER", nullable: true),
                    WorkingMemoryVersionAfter = table.Column<int>(type: "INTEGER", nullable: true),
                    FoldedMessageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    PromptTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    CompletionTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    TotalTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    TokensAreEstimated = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompressionEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConversationMessages",
                columns: table => new
                {
                    ClusterId = table.Column<long>(type: "INTEGER", nullable: false),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    RawWireJson = table.Column<string>(type: "TEXT", nullable: true),
                    TokenCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    FoldedIntoWorkingMemoryVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    IsPinnedForToolSchema = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConversationMetricsSummaries",
                columns: table => new
                {
                    ClusterId = table.Column<long>(type: "INTEGER", nullable: false),
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
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationMetricsSummaries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Conversations",
                columns: table => new
                {
                    ClusterId = table.Column<long>(type: "INTEGER", nullable: false),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationKey = table.Column<string>(type: "TEXT", nullable: false),
                    SystemPrompt = table.Column<string>(type: "TEXT", nullable: true),
                    SyncedMessageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConversationToolCatalogs",
                columns: table => new
                {
                    ClusterId = table.Column<long>(type: "INTEGER", nullable: false),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CatalogHash = table.Column<string>(type: "TEXT", nullable: false),
                    CompactIndexJson = table.Column<string>(type: "TEXT", nullable: false),
                    SnapshottedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationToolCatalogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConversationToolDefinitions",
                columns: table => new
                {
                    ClusterId = table.Column<long>(type: "INTEGER", nullable: false),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToolName = table.Column<string>(type: "TEXT", nullable: false),
                    DefinitionHash = table.Column<string>(type: "TEXT", nullable: false),
                    DefinitionJson = table.Column<string>(type: "TEXT", nullable: false),
                    HydratedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationToolDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConversationTurnMetrics",
                columns: table => new
                {
                    ClusterId = table.Column<long>(type: "INTEGER", nullable: false),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TurnIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestStartedAt = table.Column<long>(type: "INTEGER", nullable: false),
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
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationTurnMetrics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkingMemories",
                columns: table => new
                {
                    ClusterId = table.Column<long>(type: "INTEGER", nullable: false),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    TokenCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkingMemories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompressionEvents_ClusterId",
                table: "CompressionEvents",
                column: "ClusterId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompressionEvents_ConversationId_CreatedAt",
                table: "CompressionEvents",
                columns: new[] { "ConversationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_ClusterId",
                table: "ConversationMessages",
                column: "ClusterId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_ConversationId_FoldedIntoWorkingMemoryVersion",
                table: "ConversationMessages",
                columns: new[] { "ConversationId", "FoldedIntoWorkingMemoryVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_ConversationId_Sequence",
                table: "ConversationMessages",
                columns: new[] { "ConversationId", "Sequence" },
                unique: true);

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
                name: "IX_Conversations_ClusterId",
                table: "Conversations",
                column: "ClusterId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_ConversationKey",
                table: "Conversations",
                column: "ConversationKey",
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_WorkingMemories_ClusterId",
                table: "WorkingMemories",
                column: "ClusterId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkingMemories_ConversationId_Version",
                table: "WorkingMemories",
                columns: new[] { "ConversationId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompressionEvents");

            migrationBuilder.DropTable(
                name: "ConversationMessages");

            migrationBuilder.DropTable(
                name: "ConversationMetricsSummaries");

            migrationBuilder.DropTable(
                name: "Conversations");

            migrationBuilder.DropTable(
                name: "ConversationToolCatalogs");

            migrationBuilder.DropTable(
                name: "ConversationToolDefinitions");

            migrationBuilder.DropTable(
                name: "ConversationTurnMetrics");

            migrationBuilder.DropTable(
                name: "WorkingMemories");
        }
    }
}
