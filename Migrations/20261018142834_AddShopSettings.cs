using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace altinnendata_api.Migrations
{
    /// <inheritdoc />
    public partial class AddShopSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeliveryOptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    PriceNok = table.Column<int>(type: "integer", nullable: false),
                    LabelNo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    LabelEn = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DescriptionNo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    DescriptionEn = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    MaxDistanceKm = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryOptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShopSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    BuyButton = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VippsTestMode = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOptions_Method",
                table: "DeliveryOptions",
                column: "Method",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliveryOptions");

            migrationBuilder.DropTable(
                name: "ShopSettings");
        }
    }
}
