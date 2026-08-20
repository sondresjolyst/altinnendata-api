using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace altinnendata_api.Migrations
{
    /// <inheritdoc />
    public partial class StructuredCompanyAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Address",
                table: "AppSettings",
                newName: "StreetAddress");

            migrationBuilder.AddColumn<string>(
                name: "AddressLocality",
                table: "AppSettings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AddressRegion",
                table: "AppSettings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "AppSettings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            // Splits the single-line addresses entered before these columns existed. Every SET
            // reads the pre-update value of StreetAddress; a line not matching the shape stays
            // whole, which the WHERE clause takes care of.
            migrationBuilder.Sql("""
                UPDATE "AppSettings"
                SET "PostalCode"      = substring("StreetAddress" from '(\d{4})\s+[^\d,]+$'),
                    "AddressLocality" = btrim(substring("StreetAddress" from '\d{4}\s+([^\d,]+)$')),
                    "StreetAddress"   = btrim(substring("StreetAddress" from '^(.*?)[,\s]+\d{4}\s+[^\d,]+$'))
                WHERE "StreetAddress" ~ '^.*?[,\s]+\d{4}\s+[^\d,]+$';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recombine the parts before the columns holding them go away.
            migrationBuilder.Sql("""
                UPDATE "AppSettings"
                SET "StreetAddress" = concat_ws(', ',
                    NULLIF("StreetAddress", ''),
                    NULLIF(concat_ws(' ', NULLIF("PostalCode", ''), NULLIF("AddressLocality", '')), ''));
                """);

            migrationBuilder.DropColumn(
                name: "AddressLocality",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "AddressRegion",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "AppSettings");

            migrationBuilder.RenameColumn(
                name: "StreetAddress",
                table: "AppSettings",
                newName: "Address");
        }
    }
}
