using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace altinnendata_api.Migrations
{
    /// <inheritdoc />
    public partial class AddConsentRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsentRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConsentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Marketing = table.Column<bool>(type: "boolean", nullable: false),
                    NoticeVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsentRecords_ConsentId_DecidedAt",
                table: "ConsentRecords",
                columns: new[] { "ConsentId", "DecidedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsentRecords_DecidedAt",
                table: "ConsentRecords",
                column: "DecidedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsentRecords");
        }
    }
}
