using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace altinnendata_api.Migrations
{
    /// <inheritdoc />
    public partial class AddVippsWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // xmin is a Postgres system column, so Npgsql emits no SQL for it here or in Down.
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Orders",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedWebhookEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VippsWebhookRegistrations",
                columns: table => new
                {
                    Slot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsTest = table.Column<bool>(type: "boolean", nullable: false),
                    WebhookId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProtectedSecret = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VippsWebhookRegistrations", x => new { x.Slot, x.IsTest });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedWebhookEvents_ProcessedAt",
                table: "ProcessedWebhookEvents",
                column: "ProcessedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedWebhookEvents_Source_Id",
                table: "ProcessedWebhookEvents",
                columns: new[] { "Source", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "ProcessedWebhookEvents");

            migrationBuilder.DropTable(
                name: "VippsWebhookRegistrations");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Orders");
        }
    }
}
