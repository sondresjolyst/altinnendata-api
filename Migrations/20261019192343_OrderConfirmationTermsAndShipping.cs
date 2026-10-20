using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace altinnendata_api.Migrations
{
    /// <inheritdoc />
    public partial class OrderConfirmationTermsAndShipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmationAttemptedAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConfirmationAttempts",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmationSentAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ShippedAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingNote",
                table: "Orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsAcceptedIp",
                table: "Orders",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsLocale",
                table: "Orders",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsMarkdown",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsTitle",
                table: "Orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsVersion",
                table: "Orders",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            // Orders from before this migration had their confirmation without attachments. Mark them sent, so the
            // retry does not email those buyers again.
            migrationBuilder.Sql(
                """UPDATE "Orders" SET "ConfirmationSentAt" = COALESCE("AuthorizedAt", "CreatedAt") WHERE "Status" IN ('Reserved', 'Paid', 'Refunded');""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfirmationAttemptedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ConfirmationAttempts",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ConfirmationSentAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingNote",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TermsAcceptedIp",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TermsLocale",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TermsMarkdown",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TermsTitle",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TermsVersion",
                table: "Orders");
        }
    }
}
