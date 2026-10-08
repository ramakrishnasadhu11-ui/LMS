/*
===============================================================================
 LMS - Delete All Data Script
===============================================================================
 WARNING: THIS SCRIPT PERMANENTLY DELETES ALL APPLICATION DATA.
		  Take a backup before running it.

 Databases cleared:
   - LMSMasterDb   (Customers, CustomerPreferences, PricingRules,
					CustomerAdvances, LaundryOrders, LaundryItemPrices)
   - LMSIdentityDb (Tenants, TenantStoreInfos, StoreUsers, StoreConfigurations)

 After running this you must register a tenant / store user again before
 you can log in to the application.

 Table schema (EF Core) is preserved, as is the __EFMigrationsHistory table,
 so no migrations need to be re-applied.

 Identity columns are reseeded so new rows start again at 1.

 Run in SSMS / Azure Data Studio with SQLCMD mode NOT required.
===============================================================================
*/

SET NOCOUNT ON;

-------------------------------------------------------------------------------
-- 1. LMSMasterDb
-------------------------------------------------------------------------------
USE [LMSMasterDb];
GO

BEGIN TRY
	BEGIN TRANSACTION;

	DELETE FROM [dbo].[LaundryOrders];
	DELETE FROM [dbo].[CustomerAdvances];
	DELETE FROM [dbo].[LaundryItemPrices];
	DELETE FROM [dbo].[PricingRules];
	DELETE FROM [dbo].[CustomerPreferences];
	DELETE FROM [dbo].[Customers];

	COMMIT TRANSACTION;
	PRINT 'LMSMasterDb: all data deleted.';
END TRY
BEGIN CATCH
	IF XACT_STATE() <> 0
		ROLLBACK TRANSACTION;
	PRINT 'LMSMasterDb: delete failed - ' + ERROR_MESSAGE();
	THROW;
END CATCH;
GO

-- Reseed identity columns so the next inserted row gets Id = 1
DBCC CHECKIDENT ('[dbo].[Customers]',           RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('[dbo].[CustomerPreferences]', RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('[dbo].[PricingRules]',        RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('[dbo].[CustomerAdvances]',    RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('[dbo].[LaundryOrders]',       RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('[dbo].[LaundryItemPrices]',   RESEED, 0) WITH NO_INFOMSGS;
PRINT 'LMSMasterDb: identity columns reseeded.';
GO

-------------------------------------------------------------------------------
-- 2. LMSIdentityDb
-------------------------------------------------------------------------------
USE [LMSIdentityDb];
GO

BEGIN TRY
	BEGIN TRANSACTION;

	DELETE FROM [dbo].[StoreConfigurations];
	DELETE FROM [dbo].[StoreUsers];
	DELETE FROM [dbo].[TenantStoreInfos];
	DELETE FROM [dbo].[Tenants];

	COMMIT TRANSACTION;
	PRINT 'LMSIdentityDb: all data deleted.';
END TRY
BEGIN CATCH
	IF XACT_STATE() <> 0
		ROLLBACK TRANSACTION;
	PRINT 'LMSIdentityDb: delete failed - ' + ERROR_MESSAGE();
	THROW;
END CATCH;
GO

DBCC CHECKIDENT ('[dbo].[Tenants]',             RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('[dbo].[TenantStoreInfos]',    RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('[dbo].[StoreUsers]',          RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('[dbo].[StoreConfigurations]', RESEED, 0) WITH NO_INFOMSGS;
PRINT 'LMSIdentityDb: identity columns reseeded.';
GO

-------------------------------------------------------------------------------
-- 3. Verification (all counts should be 0)
-------------------------------------------------------------------------------
USE [LMSMasterDb];
GO
SELECT 'Customers' AS [Table], COUNT(*) AS [Rows] FROM [dbo].[Customers]
UNION ALL SELECT 'CustomerPreferences', COUNT(*) FROM [dbo].[CustomerPreferences]
UNION ALL SELECT 'PricingRules',        COUNT(*) FROM [dbo].[PricingRules]
UNION ALL SELECT 'CustomerAdvances',    COUNT(*) FROM [dbo].[CustomerAdvances]
UNION ALL SELECT 'LaundryOrders',       COUNT(*) FROM [dbo].[LaundryOrders]
UNION ALL SELECT 'LaundryItemPrices',   COUNT(*) FROM [dbo].[LaundryItemPrices];
GO

USE [LMSIdentityDb];
GO
SELECT 'Tenants' AS [Table], COUNT(*) AS [Rows] FROM [dbo].[Tenants]
UNION ALL SELECT 'TenantStoreInfos',    COUNT(*) FROM [dbo].[TenantStoreInfos]
UNION ALL SELECT 'StoreUsers',          COUNT(*) FROM [dbo].[StoreUsers]
UNION ALL SELECT 'StoreConfigurations', COUNT(*) FROM [dbo].[StoreConfigurations];
GO
