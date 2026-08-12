using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace altinnendata_api.Migrations
{
    /// <inheritdoc />
    public partial class DeriveCoverFromGallery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PcBuilds_ContentImages_CoverImageId",
                table: "PcBuilds");

            migrationBuilder.DropIndex(
                name: "IX_PcBuilds_CoverImageId",
                table: "PcBuilds");

            migrationBuilder.DropColumn(
                name: "CoverImageId",
                table: "PcBuilds");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CoverImageId",
                table: "PcBuilds",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PcBuilds_CoverImageId",
                table: "PcBuilds",
                column: "CoverImageId");

            migrationBuilder.AddForeignKey(
                name: "FK_PcBuilds_ContentImages_CoverImageId",
                table: "PcBuilds",
                column: "CoverImageId",
                principalTable: "ContentImages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
