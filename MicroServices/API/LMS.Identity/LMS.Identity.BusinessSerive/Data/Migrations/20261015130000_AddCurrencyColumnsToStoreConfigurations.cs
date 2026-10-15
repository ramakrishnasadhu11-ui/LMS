using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Identity.BusinessSerive.Data.Migrations
{
    public partial class AddCurrencyColumnsToStoreConfigurations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add currency-related columns if they don't already exist to support new registration fields.
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.StoreConfigurations','CurrencyCode') IS NULL
BEGIN
    ALTER TABLE dbo.StoreConfigurations ADD CurrencyCode NVARCHAR(16) NULL;
END
IF COL_LENGTH('dbo.StoreConfigurations','CurrencySymbol') IS NULL
BEGIN
    ALTER TABLE dbo.StoreConfigurations ADD CurrencySymbol NVARCHAR(16) NULL;
END
IF COL_LENGTH('dbo.StoreConfigurations','CurrencyLocale') IS NULL
BEGIN
    ALTER TABLE dbo.StoreConfigurations ADD CurrencyLocale NVARCHAR(32) NULL;
END
IF COL_LENGTH('dbo.StoreConfigurations','CurrencyDecimalDigits') IS NULL
BEGIN
    ALTER TABLE dbo.StoreConfigurations ADD CurrencyDecimalDigits INT NULL;
END
");

            // Backfill existing rows from ConfigurationJson for legacy records using SQL Server JSON functions.
            migrationBuilder.Sql(@"
UPDATE StoreConfigurations
SET CurrencyCode = JSON_VALUE(ConfigurationJson, '$.CurrencyCode'),
    CurrencySymbol = JSON_VALUE(ConfigurationJson, '$.CurrencySymbol'),
    CurrencyLocale = JSON_VALUE(ConfigurationJson, '$.CurrencyLocale'),
    CurrencyDecimalDigits = TRY_CAST(JSON_VALUE(ConfigurationJson, '$.CurrencyDecimalDigits') AS int)
WHERE ConfigurationJson IS NOT NULL;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove currency-related columns if present (reverse of Up)
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.StoreConfigurations','CurrencyDecimalDigits') IS NOT NULL
BEGIN
    ALTER TABLE dbo.StoreConfigurations DROP COLUMN CurrencyDecimalDigits;
END
IF COL_LENGTH('dbo.StoreConfigurations','CurrencyLocale') IS NOT NULL
BEGIN
    ALTER TABLE dbo.StoreConfigurations DROP COLUMN CurrencyLocale;
END
IF COL_LENGTH('dbo.StoreConfigurations','CurrencySymbol') IS NOT NULL
BEGIN
    ALTER TABLE dbo.StoreConfigurations DROP COLUMN CurrencySymbol;
END
IF COL_LENGTH('dbo.StoreConfigurations','CurrencyCode') IS NOT NULL
BEGIN
    ALTER TABLE dbo.StoreConfigurations DROP COLUMN CurrencyCode;
END
");
        }
    }
}
