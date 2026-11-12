/*
   Review-only schema change for the visual laundry catalogue.
   Verify the production schema and obtain deployment approval before execution.
   This script is intentionally not invoked by the application or migration tooling.
*/
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'[dbo].[LaundryItemPrices]', N'U') IS NULL
	THROW 50001, 'LaundryItemPrices does not exist; verify the target database.', 1;

IF OBJECT_ID(N'[dbo].[StoreItemMaster]', N'U') IS NULL
	THROW 50002, 'StoreItemMaster does not exist; verify the target database.', 1;

IF COL_LENGTH(N'[dbo].[LaundryItemPrices]', N'ImagePath') IS NULL
	ALTER TABLE [dbo].[LaundryItemPrices] ADD [ImagePath] [nvarchar](512) NULL;

IF COL_LENGTH(N'[dbo].[StoreItemMaster]', N'ImagePath') IS NULL
	ALTER TABLE [dbo].[StoreItemMaster] ADD [ImagePath] [nvarchar](512) NULL;

COMMIT TRANSACTION;