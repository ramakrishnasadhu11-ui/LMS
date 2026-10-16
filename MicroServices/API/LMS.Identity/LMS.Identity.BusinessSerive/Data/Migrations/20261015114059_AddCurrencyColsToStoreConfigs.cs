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
            // Idempotent add of currency-related columns to existing tables to avoid duplicate-column errors
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.StoreConfigurations','CurrencyCode') IS NULL
    ALTER TABLE dbo.StoreConfigurations ADD CurrencyCode NVARCHAR(16) NULL;
IF COL_LENGTH('dbo.StoreConfigurations','CurrencySymbol') IS NULL
    ALTER TABLE dbo.StoreConfigurations ADD CurrencySymbol NVARCHAR(16) NULL;
IF COL_LENGTH('dbo.StoreConfigurations','CurrencyLocale') IS NULL
    ALTER TABLE dbo.StoreConfigurations ADD CurrencyLocale NVARCHAR(32) NULL;
IF COL_LENGTH('dbo.StoreConfigurations','CurrencyDecimalDigits') IS NULL
    ALTER TABLE dbo.StoreConfigurations ADD CurrencyDecimalDigits INT NULL;

IF COL_LENGTH('dbo.TenantStoreInfos','CurrencyCode') IS NULL
    ALTER TABLE dbo.TenantStoreInfos ADD CurrencyCode NVARCHAR(16) NULL;
IF COL_LENGTH('dbo.TenantStoreInfos','CurrencySymbol') IS NULL
    ALTER TABLE dbo.TenantStoreInfos ADD CurrencySymbol NVARCHAR(16) NULL;
IF COL_LENGTH('dbo.TenantStoreInfos','CurrencyLocale') IS NULL
    ALTER TABLE dbo.TenantStoreInfos ADD CurrencyLocale NVARCHAR(32) NULL;
IF COL_LENGTH('dbo.TenantStoreInfos','CurrencyDecimalDigits') IS NULL
    ALTER TABLE dbo.TenantStoreInfos ADD CurrencyDecimalDigits INT NULL;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop currency columns only if present
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.StoreConfigurations','CurrencyDecimalDigits') IS NOT NULL
    ALTER TABLE dbo.StoreConfigurations DROP COLUMN CurrencyDecimalDigits;
IF COL_LENGTH('dbo.StoreConfigurations','CurrencyLocale') IS NOT NULL
    ALTER TABLE dbo.StoreConfigurations DROP COLUMN CurrencyLocale;
IF COL_LENGTH('dbo.StoreConfigurations','CurrencySymbol') IS NOT NULL
    ALTER TABLE dbo.StoreConfigurations DROP COLUMN CurrencySymbol;
IF COL_LENGTH('dbo.StoreConfigurations','CurrencyCode') IS NOT NULL
    ALTER TABLE dbo.StoreConfigurations DROP COLUMN CurrencyCode;

IF COL_LENGTH('dbo.TenantStoreInfos','CurrencyDecimalDigits') IS NOT NULL
    ALTER TABLE dbo.TenantStoreInfos DROP COLUMN CurrencyDecimalDigits;
IF COL_LENGTH('dbo.TenantStoreInfos','CurrencyLocale') IS NOT NULL
    ALTER TABLE dbo.TenantStoreInfos DROP COLUMN CurrencyLocale;
IF COL_LENGTH('dbo.TenantStoreInfos','CurrencySymbol') IS NOT NULL
    ALTER TABLE dbo.TenantStoreInfos DROP COLUMN CurrencySymbol;
IF COL_LENGTH('dbo.TenantStoreInfos','CurrencyCode') IS NOT NULL
    ALTER TABLE dbo.TenantStoreInfos DROP COLUMN CurrencyCode;
");
        }
    }
}
