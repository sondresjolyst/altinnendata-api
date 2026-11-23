using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace altinnendata_api.Migrations
{
    /// <summary>Sets payments left as NOTFOUND by an earlier build of payment polling back to CREATED.</summary>
    public partial class ResetNotFoundPaymentStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"OrderPayments\" SET \"State\" = 'CREATED' WHERE \"State\" = 'NOTFOUND';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
