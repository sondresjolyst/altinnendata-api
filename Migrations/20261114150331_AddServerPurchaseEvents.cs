using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace altinnendata_api.Migrations
{
    /// <inheritdoc />
    public partial class AddServerPurchaseEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MetaCapiToken",
                table: "ShopSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TikTokEventsToken",
                table: "ShopSettings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientIp",
                table: "Orders",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientUserAgent",
                table: "Orders",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EventSourceUrl",
                table: "Orders",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Fbc",
                table: "Orders",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Fbp",
                table: "Orders",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MarketingConsentId",
                table: "Orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MarketingEventsDueBy",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MarketingNoticeVersion",
                table: "Orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MetaEventAttemptedAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MetaEventAttempts",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "MetaEventSentAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TikTokEventAttemptedAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TikTokEventAttempts",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "TikTokEventSentAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ttclid",
                table: "Orders",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ttp",
                table: "Orders",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MetaCapiToken",
                table: "ShopSettings");

            migrationBuilder.DropColumn(
                name: "TikTokEventsToken",
                table: "ShopSettings");

            migrationBuilder.DropColumn(
                name: "ClientIp",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ClientUserAgent",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "EventSourceUrl",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Fbc",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Fbp",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "MarketingConsentId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "MarketingEventsDueBy",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "MarketingNoticeVersion",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "MetaEventAttemptedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "MetaEventAttempts",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "MetaEventSentAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TikTokEventAttemptedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TikTokEventAttempts",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TikTokEventSentAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Ttclid",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Ttp",
                table: "Orders");
        }
    }
}
