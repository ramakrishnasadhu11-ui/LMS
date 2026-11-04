/*
  Excel Laundry Services - Phase A + Phase B Identity schema migration
  Target: SQL Server (SmarterASP.NET hosted production Identity database)
  Scope: Add nullable columns to dbo.Tenants with migration-history guards
  Migrations:
	1) 20261104075223_AddTenantCountryCode
	2) 20261104082059_AddTenantLocalizationFields

  Safety:
  - Idempotent: checks __EFMigrationsHistory and column existence before changes
  - Non-destructive: no DROP/TRUNCATE/DELETE/UPDATE of tenant data
  - Does not execute automatically unless run manually by DBA/operator
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
	BEGIN TRANSACTION;

	IF OBJECT_ID(N'[dbo].[__EFMigrationsHistory]', N'U') IS NULL
	BEGIN
		THROW 51000, '__EFMigrationsHistory table does not exist. Aborting migration script.', 1;
	END;

	/* ---------------------------------------------------------------------
	   Migration: 20261104075223_AddTenantCountryCode
	   --------------------------------------------------------------------- */
	IF NOT EXISTS (
		SELECT 1
		FROM [dbo].[__EFMigrationsHistory]
		WHERE [MigrationId] = N'20261104075223_AddTenantCountryCode'
	)
	BEGIN
		IF COL_LENGTH(N'[dbo].[Tenants]', N'CountryCode') IS NULL
		BEGIN
			ALTER TABLE [dbo].[Tenants]
			ADD [CountryCode] NVARCHAR(2) NULL;
		END;

		INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
		VALUES (N'20261104075223_AddTenantCountryCode', N'8.0.31');
	END;

	IF COL_LENGTH(N'[dbo].[Tenants]', N'CountryCode') IS NULL
	BEGIN
		THROW 51001, 'Verification failed for migration 20261104075223_AddTenantCountryCode. Expected column [dbo].[Tenants].[CountryCode] was not found.', 1;
	END;

	/* ---------------------------------------------------------------------
	   Migration: 20261104082059_AddTenantLocalizationFields
	   --------------------------------------------------------------------- */
	IF NOT EXISTS (
		SELECT 1
		FROM [dbo].[__EFMigrationsHistory]
		WHERE [MigrationId] = N'20261104082059_AddTenantLocalizationFields'
	)
	BEGIN
		IF COL_LENGTH(N'[dbo].[Tenants]', N'PhoneDialingCode') IS NULL
		BEGIN
			ALTER TABLE [dbo].[Tenants]
			ADD [PhoneDialingCode] NVARCHAR(8) NULL;
		END;

		IF COL_LENGTH(N'[dbo].[Tenants]', N'TimeZoneIana') IS NULL
		BEGIN
			ALTER TABLE [dbo].[Tenants]
			ADD [TimeZoneIana] NVARCHAR(64) NULL;
		END;

		INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
		VALUES (N'20261104082059_AddTenantLocalizationFields', N'8.0.31');
	END;

	IF COL_LENGTH(N'[dbo].[Tenants]', N'PhoneDialingCode') IS NULL
	BEGIN
		THROW 51002, 'Verification failed for migration 20261104082059_AddTenantLocalizationFields. Expected column [dbo].[Tenants].[PhoneDialingCode] was not found.', 1;
	END;

	IF COL_LENGTH(N'[dbo].[Tenants]', N'TimeZoneIana') IS NULL
	BEGIN
		THROW 51003, 'Verification failed for migration 20261104082059_AddTenantLocalizationFields. Expected column [dbo].[Tenants].[TimeZoneIana] was not found.', 1;
	END;

	COMMIT TRANSACTION;
END TRY
BEGIN CATCH
	IF @@TRANCOUNT > 0
		ROLLBACK TRANSACTION;

	THROW;
END CATCH;
