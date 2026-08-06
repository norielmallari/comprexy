using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comprexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModelPricingCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModelPricingEntries",
                columns: table => new
                {
                    ClusterId = table.Column<long>(type: "INTEGER", nullable: false),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ModelKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DisplayLabel = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, defaultValue: "USD"),
                    InputUsdPer1M = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    OutputUsdPer1M = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    CachedInputUsdPer1M = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: true),
                    CachedOutputUsdPer1M = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelPricingEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModelPricingEntries_ClusterId",
                table: "ModelPricingEntries",
                column: "ClusterId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModelPricingEntries_IsActive_SortOrder",
                table: "ModelPricingEntries",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ModelPricingEntries_ModelKey",
                table: "ModelPricingEntries",
                column: "ModelKey",
                unique: true);

            // Locked presentation catalog (USD per 1M; post-Sep Sonnet $3/$15). Cached columns reserved unused.
            migrationBuilder.InsertData(
                table: "ModelPricingEntries",
                columns: new[]
                {
                    "ClusterId",
                    "Id",
                    "ModelKey",
                    "DisplayLabel",
                    "CurrencyCode",
                    "InputUsdPer1M",
                    "OutputUsdPer1M",
                    "CachedInputUsdPer1M",
                    "CachedOutputUsdPer1M",
                    "SortOrder",
                    "IsActive"
                },
                values: new object[,]
                {
                    {
                        1L,
                        new Guid("a1000000-0000-4000-8000-000000000001"),
                        "local",
                        "Local",
                        "USD",
                        0m,
                        0m,
                        null,
                        null,
                        0,
                        true
                    },
                    {
                        2L,
                        new Guid("a1000000-0000-4000-8000-000000000002"),
                        "claude-haiku-4-5",
                        "Claude Haiku 4.5",
                        "USD",
                        1m,
                        5m,
                        null,
                        null,
                        1,
                        true
                    },
                    {
                        3L,
                        new Guid("a1000000-0000-4000-8000-000000000003"),
                        "claude-sonnet-5",
                        "Claude Sonnet 5",
                        "USD",
                        3m,
                        15m,
                        null,
                        null,
                        2,
                        true
                    },
                    {
                        4L,
                        new Guid("a1000000-0000-4000-8000-000000000004"),
                        "claude-opus-5",
                        "Claude Opus 5",
                        "USD",
                        5m,
                        25m,
                        null,
                        null,
                        3,
                        true
                    },
                    {
                        5L,
                        new Guid("a1000000-0000-4000-8000-000000000005"),
                        "claude-fable-5",
                        "Claude Fable 5",
                        "USD",
                        10m,
                        50m,
                        null,
                        null,
                        4,
                        true
                    },
                    {
                        6L,
                        new Guid("a1000000-0000-4000-8000-000000000006"),
                        "gpt-5.5",
                        "GPT-5.5",
                        "USD",
                        5m,
                        30m,
                        null,
                        null,
                        5,
                        true
                    },
                    {
                        7L,
                        new Guid("a1000000-0000-4000-8000-000000000007"),
                        "gpt-5.5-pro",
                        "GPT-5.5 Pro",
                        "USD",
                        30m,
                        180m,
                        null,
                        null,
                        6,
                        true
                    },
                    {
                        8L,
                        new Guid("a1000000-0000-4000-8000-000000000008"),
                        "gpt-5.6-sol",
                        "GPT-5.6 Sol",
                        "USD",
                        5m,
                        30m,
                        null,
                        null,
                        7,
                        true
                    },
                    {
                        9L,
                        new Guid("a1000000-0000-4000-8000-000000000009"),
                        "gpt-5.6-terra",
                        "GPT-5.6 Terra",
                        "USD",
                        2m,
                        12m,
                        null,
                        null,
                        8,
                        true
                    },
                    {
                        10L,
                        new Guid("a1000000-0000-4000-8000-00000000000a"),
                        "gpt-5.6-luna",
                        "GPT-5.6 Luna",
                        "USD",
                        0.20m,
                        1.20m,
                        null,
                        null,
                        9,
                        true
                    }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModelPricingEntries");
        }
    }
}
