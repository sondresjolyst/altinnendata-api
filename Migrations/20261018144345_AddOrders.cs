using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace altinnendata_api.Migrations
{
    /// <inheritdoc />
    public partial class AddOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HeldByOrderId",
                table: "PcBuilds",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublicToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Locale = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    BuyerName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    BuyerEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BuyerPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StreetAddress = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PostalCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeliveryMethod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DeliveryLabel = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DeliveryPriceInOre = table.Column<int>(type: "integer", nullable: false),
                    WithinDistanceConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PickupNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PcBuildId = table.Column<int>(type: "integer", nullable: true),
                    BuildTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BuildSlug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    BuildPriceInOre = table.Column<int>(type: "integer", nullable: false),
                    TotalInOre = table.Column<int>(type: "integer", nullable: false),
                    CapturedInOre = table.Column<int>(type: "integer", nullable: false),
                    RefundedInOre = table.Column<int>(type: "integer", nullable: false),
                    IsTest = table.Column<bool>(type: "boolean", nullable: false),
                    CaptureGuaranteedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FikenInvoiceNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    TermsAcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AuthorizedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RefundedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Orders_PcBuilds_PcBuildId",
                        column: x => x.PcBuildId,
                        principalTable: "PcBuilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PcBuilds_HeldByOrderId",
                table: "PcBuilds",
                column: "HeldByOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CreatedAt",
                table: "Orders",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PcBuildId",
                table: "Orders",
                column: "PcBuildId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PublicToken",
                table: "Orders",
                column: "PublicToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Reference",
                table: "Orders",
                column: "Reference",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PcBuilds_Orders_HeldByOrderId",
                table: "PcBuilds",
                column: "HeldByOrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PcBuilds_Orders_HeldByOrderId",
                table: "PcBuilds");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_PcBuilds_HeldByOrderId",
                table: "PcBuilds");

            migrationBuilder.DropColumn(
                name: "HeldByOrderId",
                table: "PcBuilds");
        }
    }
}
