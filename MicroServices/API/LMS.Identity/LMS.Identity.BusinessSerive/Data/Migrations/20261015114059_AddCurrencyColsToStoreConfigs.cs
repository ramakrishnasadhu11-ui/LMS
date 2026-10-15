using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Identity.BusinessSerive.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrencyColsToStoreConfigs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // This migration only adds currency-related columns to existing tables.
            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "StoreConfigurations",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrencySymbol",
                table: "StoreConfigurations",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyLocale",
                table: "StoreConfigurations",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrencyDecimalDigits",
                table: "StoreConfigurations",
                type: "int",
                nullable: true);

            // Also add to TenantStoreInfos to keep parity
            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "TenantStoreInfos",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrencySymbol",
                table: "TenantStoreInfos",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyLocale",
                table: "TenantStoreInfos",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrencyDecimalDigits",
                table: "TenantStoreInfos",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "StoreConfigurations");

            migrationBuilder.DropColumn(
                name: "CurrencySymbol",
                table: "StoreConfigurations");

            migrationBuilder.DropColumn(
                name: "CurrencyLocale",
                table: "StoreConfigurations");

            migrationBuilder.DropColumn(
                name: "CurrencyDecimalDigits",
                table: "StoreConfigurations");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "TenantStoreInfos");

            migrationBuilder.DropColumn(
                name: "CurrencySymbol",
                table: "TenantStoreInfos");

            migrationBuilder.DropColumn(
                name: "CurrencyLocale",
                table: "TenantStoreInfos");

            migrationBuilder.DropColumn(
                name: "CurrencyDecimalDigits",
                table: "TenantStoreInfos");
        }
    }
}
