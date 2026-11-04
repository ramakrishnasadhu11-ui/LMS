using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Identity.BusinessSerive.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddActivationIdempotencyKeyToSubscriptionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActivationIdempotencyKey",
                table: "TenantSubscriptionStateHistories",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_TenantSubscriptionStateHistories_ActivationKey",
                table: "TenantSubscriptionStateHistories",
                column: "ActivationIdempotencyKey",
                unique: true,
                filter: "[ActivationIdempotencyKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_TenantSubscriptionStateHistories_ActivationKey",
                table: "TenantSubscriptionStateHistories");

            migrationBuilder.DropColumn(
                name: "ActivationIdempotencyKey",
                table: "TenantSubscriptionStateHistories");
        }
    }
}
