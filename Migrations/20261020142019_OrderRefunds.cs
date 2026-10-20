using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace altinnendata_api.Migrations
{
    /// <inheritdoc />
    public partial class OrderRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderRefunds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderId = table.Column<int>(type: "integer", nullable: false),
                    AmountInOre = table.Column<int>(type: "integer", nullable: false),
                    RefundedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderRefunds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderRefunds_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderRefunds_OrderId",
                table: "OrderRefunds",
                column: "OrderId");

            // Refunds recorded before this table existed keep their total as one row.
            migrationBuilder.Sql("""
                INSERT INTO "OrderRefunds" ("OrderId", "AmountInOre", "RefundedAt", "IdempotencyKey", "Source")
                SELECT "Id", "RefundedInOre", COALESCE("RefundedAt", "UpdatedAt"), NULL, 'Migrated'
                FROM "Orders" WHERE "RefundedInOre" > 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderRefunds");
        }
    }
}
