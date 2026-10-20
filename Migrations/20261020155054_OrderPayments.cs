using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace altinnendata_api.Migrations
{
    /// <inheritdoc />
    public partial class OrderPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentLinkExpiresAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentLinkSentAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentLinkTokenHash",
                table: "Orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentLinkUsedAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VippsReference",
                table: "Orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "OrderPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderId = table.Column<int>(type: "integer", nullable: false),
                    VippsReference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderPayments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Every order so far has one Vipps payment, under its order number.
            migrationBuilder.Sql("""UPDATE "Orders" SET "VippsReference" = "Reference";""");
            migrationBuilder.Sql("""
                INSERT INTO "OrderPayments" ("OrderId", "VippsReference", "State", "CreatedAt")
                SELECT "Id", "Reference",
                    CASE "Status"
                        WHEN 'Reserved' THEN 'AUTHORIZED'
                        WHEN 'Paid' THEN 'CAPTURED'
                        WHEN 'Refunded' THEN 'REFUNDED'
                        WHEN 'Cancelled' THEN 'CANCELLED'
                        WHEN 'Failed' THEN 'FAILED'
                        ELSE 'CREATED'
                    END,
                    "CreatedAt"
                FROM "Orders";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PaymentLinkTokenHash",
                table: "Orders",
                column: "PaymentLinkTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_VippsReference",
                table: "Orders",
                column: "VippsReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_OrderId",
                table: "OrderPayments",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_VippsReference",
                table: "OrderPayments",
                column: "VippsReference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderPayments");

            migrationBuilder.DropIndex(
                name: "IX_Orders_PaymentLinkTokenHash",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_VippsReference",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentLinkExpiresAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentLinkSentAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentLinkTokenHash",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentLinkUsedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "VippsReference",
                table: "Orders");
        }
    }
}
