using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Identity.BusinessSerive.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantApprovalStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovalDate",
                table: "Tenants",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalReason",
                table: "Tenants",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalStatus",
                table: "Tenants",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "Tenants",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_ApprovalStatus",
                table: "Tenants",
                column: "ApprovalStatus");

            // Tenants that already existed before the approval workflow was introduced are
            // auto-approved so they are never locked out of the application.
            migrationBuilder.Sql(
                "UPDATE [Tenants] SET [ApprovalStatus] = 'Approved' WHERE [ApprovalStatus] IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tenants_ApprovalStatus",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ApprovalDate",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ApprovalReason",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "Tenants");
        }
    }
}
