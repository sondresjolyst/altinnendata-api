using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace altinnendata_api.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildGalleryAndFinnLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SectionsJson",
                table: "PcBuildTranslations");

            migrationBuilder.AddColumn<string>(
                name: "FinnUrl",
                table: "PcBuilds",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "PcBuildTranslations",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PcBuildImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PcBuildId = table.Column<int>(type: "integer", nullable: false),
                    ContentImageId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PcBuildImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PcBuildImages_ContentImages_ContentImageId",
                        column: x => x.ContentImageId,
                        principalTable: "ContentImages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PcBuildImages_PcBuilds_PcBuildId",
                        column: x => x.PcBuildId,
                        principalTable: "PcBuilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PcBuildImages_ContentImageId",
                table: "PcBuildImages",
                column: "ContentImageId");

            migrationBuilder.CreateIndex(
                name: "IX_PcBuildImages_PcBuildId_ContentImageId",
                table: "PcBuildImages",
                columns: new[] { "PcBuildId", "ContentImageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PcBuildImages");

            migrationBuilder.DropColumn(
                name: "FinnUrl",
                table: "PcBuilds");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "PcBuildTranslations");

            migrationBuilder.AddColumn<string>(
                name: "SectionsJson",
                table: "PcBuildTranslations",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
