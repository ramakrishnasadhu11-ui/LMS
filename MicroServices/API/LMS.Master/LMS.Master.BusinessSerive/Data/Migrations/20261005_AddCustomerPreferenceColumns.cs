using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Master.BusinessSerive.Data.Migrations
{
    public partial class AddCustomerPreferenceColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnableSmsNotifications",
                table: "CustomerPreferences",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnableEmailNotifications",
                table: "CustomerPreferences",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AutoGenerateCustomerCode",
                table: "CustomerPreferences",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequirePhoneNumber",
                table: "CustomerPreferences",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequireEmail",
                table: "CustomerPreferences",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllowDuplicatePhoneNumber",
                table: "CustomerPreferences",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PickupReminderHours",
                table: "CustomerPreferences",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LoyaltyPointsPerOrder",
                table: "CustomerPreferences",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnableSmsNotifications",
                table: "CustomerPreferences");

            migrationBuilder.DropColumn(
                name: "EnableEmailNotifications",
                table: "CustomerPreferences");

            migrationBuilder.DropColumn(
                name: "AutoGenerateCustomerCode",
                table: "CustomerPreferences");

            migrationBuilder.DropColumn(
                name: "RequirePhoneNumber",
                table: "CustomerPreferences");

            migrationBuilder.DropColumn(
                name: "RequireEmail",
                table: "CustomerPreferences");

            migrationBuilder.DropColumn(
                name: "AllowDuplicatePhoneNumber",
                table: "CustomerPreferences");

            migrationBuilder.DropColumn(
                name: "PickupReminderHours",
                table: "CustomerPreferences");

            migrationBuilder.DropColumn(
                name: "LoyaltyPointsPerOrder",
                table: "CustomerPreferences");
        }
    }
}
