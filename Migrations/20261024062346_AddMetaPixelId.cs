using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace altinnendata_api.Migrations
{
    /// <inheritdoc />
    public partial class AddMetaPixelId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MetaPixelId",
                table: "ShopSettings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MetaPixelId",
                table: "ShopSettings");
        }
    }
}
