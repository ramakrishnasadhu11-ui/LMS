using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Identity.BusinessSerive.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantLocalizationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhoneDialingCode",
                table: "Tenants",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneIana",
                table: "Tenants",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhoneDialingCode",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TimeZoneIana",
                table: "Tenants");
        }
    }
}
