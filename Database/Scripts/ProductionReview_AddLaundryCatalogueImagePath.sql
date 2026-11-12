/*
   REVIEW ONLY - production schema change proposal.

   Purpose:
	 Add the nullable ImagePath columns required by the current Master API
	 projections and EF model.

   Safety:
	 This file is not invoked by the application, migrations, startup, or tests.
	 Obtain production deployment approval and validate the target database before
	 executing it through the controlled production change process.
	 Do not run this file from the development application.
*/
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'[dbo].[LaundryItemPrices]', N'U') IS NULL
	THROW 50001, 'LaundryItemPrices does not exist; stop and verify the production target.', 1;

IF OBJECT_ID(N'[dbo].[StoreItemMaster]', N'U') IS NULL
	THROW 50002, 'StoreItemMaster does not exist; stop and verify the production target.', 1;

IF COL_LENGTH(N'[dbo].[LaundryItemPrices]', N'ImagePath') IS NULL
	ALTER TABLE [dbo].[LaundryItemPrices]
		ADD [ImagePath] [nvarchar](512) NULL;

IF COL_LENGTH(N'[dbo].[StoreItemMaster]', N'ImagePath') IS NULL
	ALTER TABLE [dbo].[StoreItemMaster]
		ADD [ImagePath] [nvarchar](512) NULL;

COMMIT TRANSACTION;
