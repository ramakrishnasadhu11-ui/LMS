using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Identity.BusinessSerive.Data.Migrations
{
    public partial class AlterTenantPhoneNumberToString : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Alter PhoneNumber column on Tenants from int to nvarchar(max) to match the TenantEntity/DTO types.
            // Use a safe SQL path: create temp column, migrate data with conversion, drop old column, rename temp.

            // Add a new temporary column with the target type
            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber_temp",
                table: "Tenants",
                type: "nvarchar(max)",
                nullable: true);

            // Copy existing int values into the new column as strings
            migrationBuilder.Sql("UPDATE Tenants SET PhoneNumber_temp = CAST(PhoneNumber AS nvarchar(max)) WHERE PhoneNumber IS NOT NULL");

            // Drop the old column (int)
            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "Tenants");

            // Rename temp column to PhoneNumber
            migrationBuilder.RenameColumn(
                name: "PhoneNumber_temp",
                table: "Tenants",
                newName: "PhoneNumber");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse: create an int column and try to convert where possible, otherwise set 0.
            migrationBuilder.AddColumn<int>(
                name: "PhoneNumber_temp",
                table: "Tenants",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Try to convert numeric strings back to int; non-numeric becomes 0
            migrationBuilder.Sql(@"
UPDATE Tenants
SET PhoneNumber_temp = CASE WHEN TRY_CAST(PhoneNumber AS int) IS NOT NULL THEN TRY_CAST(PhoneNumber AS int) ELSE 0 END
WHERE PhoneNumber IS NOT NULL
");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "Tenants");

            migrationBuilder.RenameColumn(
                name: "PhoneNumber_temp",
                table: "Tenants",
                newName: "PhoneNumber");
        }
    }
}
