using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace altinnendata_api.Migrations
{
    /// <inheritdoc />
    public partial class BuildClasses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BuildClassId",
                table: "PcBuilds",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BuildClasses",
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
                    table.PrimaryKey("PK_BuildClasses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BuildClassTranslations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BuildClassId = table.Column<int>(type: "integer", nullable: false),
                    Locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildClassTranslations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildClassTranslations_BuildClasses_BuildClassId",
                        column: x => x.BuildClassId,
                        principalTable: "BuildClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PcBuilds_BuildClassId",
                table: "PcBuilds",
                column: "BuildClassId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildClassTranslations_BuildClassId_Locale",
                table: "BuildClassTranslations",
                columns: new[] { "BuildClassId", "Locale" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BuildClasses_Key",
                table: "BuildClasses",
                column: "Key",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PcBuilds_BuildClasses_BuildClassId",
                table: "PcBuilds",
                column: "BuildClassId",
                principalTable: "BuildClasses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PcBuilds_BuildClasses_BuildClassId",
                table: "PcBuilds");

            migrationBuilder.DropTable(
                name: "BuildClassTranslations");

            migrationBuilder.DropTable(
                name: "BuildClasses");

            migrationBuilder.DropIndex(
                name: "IX_PcBuilds_BuildClassId",
                table: "PcBuilds");

            migrationBuilder.DropColumn(
                name: "BuildClassId",
                table: "PcBuilds");
        }
    }
}
