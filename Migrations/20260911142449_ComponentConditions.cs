using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace altinnendata_api.Migrations
{
    /// <inheritdoc />
    public partial class ComponentConditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ComponentConditionId",
                table: "PcBuildComponents",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ComponentConditions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComponentConditions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ComponentConditionTranslations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ComponentConditionId = table.Column<int>(type: "integer", nullable: false),
                    Locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComponentConditionTranslations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComponentConditionTranslations_ComponentConditions_Componen~",
                        column: x => x.ComponentConditionId,
                        principalTable: "ComponentConditions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PcBuildComponents_ComponentConditionId",
                table: "PcBuildComponents",
                column: "ComponentConditionId");

            migrationBuilder.CreateIndex(
                name: "IX_ComponentConditionTranslations_ComponentConditionId_Locale",
                table: "ComponentConditionTranslations",
                columns: new[] { "ComponentConditionId", "Locale" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComponentConditions_Key",
                table: "ComponentConditions",
                column: "Key",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PcBuildComponents_ComponentConditions_ComponentConditionId",
                table: "PcBuildComponents",
                column: "ComponentConditionId",
                principalTable: "ComponentConditions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PcBuildComponents_ComponentConditions_ComponentConditionId",
                table: "PcBuildComponents");

            migrationBuilder.DropTable(
                name: "ComponentConditionTranslations");

            migrationBuilder.DropTable(
                name: "ComponentConditions");

            migrationBuilder.DropIndex(
                name: "IX_PcBuildComponents_ComponentConditionId",
                table: "PcBuildComponents");

            migrationBuilder.DropColumn(
                name: "ComponentConditionId",
                table: "PcBuildComponents");
        }
    }
}
