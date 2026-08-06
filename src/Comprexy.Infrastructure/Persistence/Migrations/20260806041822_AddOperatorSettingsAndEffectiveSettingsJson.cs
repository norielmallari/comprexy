using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comprexy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperatorSettingsAndEffectiveSettingsJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EffectiveSettingsJson",
                table: "Conversations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OperatorSettings",
                columns: table => new
                {
                    ClusterId = table.Column<long>(type: "INTEGER", nullable: false),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    SettingsJson = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatorSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperatorSettings_ClusterId",
                table: "OperatorSettings",
                column: "ClusterId",
                unique: true);

            // Singleton row: revision 0 / empty allowlisted JSON.
            migrationBuilder.InsertData(
                table: "OperatorSettings",
                columns: new[] { "ClusterId", "Id", "Revision", "SettingsJson", "UpdatedAt" },
                values: new object[]
                {
                    1L,
                    new Guid("a1000001-0000-4000-8000-000000000001"),
                    0L,
                    "{}",
                    0L
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperatorSettings");

            migrationBuilder.DropColumn(
                name: "EffectiveSettingsJson",
                table: "Conversations");
        }
    }
}
