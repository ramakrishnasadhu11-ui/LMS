SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

/*
	Purpose:
	  Pre-deployment schema preparation for piece-tag order creation.
	  Idempotently ensures required tables/indexes used by CreateLaundryOrder (piece mode).

	Safety:
	  - No data updates/deletes are performed.
	  - Duplicate TagNo values are detected and surfaced; script does not auto-remediate data.
*/

BEGIN TRY
	BEGIN TRAN;

	IF OBJECT_ID(N'[dbo].[LaundryOrderItems]', N'U') IS NULL
	BEGIN
		CREATE TABLE [dbo].[LaundryOrderItems]
		(
			[Id] [int] IDENTITY(1,1) NOT NULL,
			[TenantName] [nvarchar](256) NOT NULL,
			[StoreCode] [nvarchar](64) NOT NULL,
			[OrderNo] [nvarchar](64) NOT NULL,
			[ServiceType] [nvarchar](64) NULL,
			[Category] [nvarchar](64) NULL,
			[ItemName] [nvarchar](128) NULL,
			[UnitPrice] [decimal](18,2) NOT NULL CONSTRAINT [DF_LaundryOrderItems_UnitPrice] DEFAULT ((0)),
			[PieceNo] [int] NOT NULL CONSTRAINT [DF_LaundryOrderItems_PieceNo] DEFAULT ((1)),
			[TagNo] [nvarchar](32) NULL,
			[CreatedDate] [datetime2](7) NOT NULL,
			CONSTRAINT [PK_LaundryOrderItems] PRIMARY KEY CLUSTERED ([Id] ASC)
		);
	END;

	IF OBJECT_ID(N'[dbo].[BarcodeTagSettings]', N'U') IS NULL
	BEGIN
		CREATE TABLE [dbo].[BarcodeTagSettings]
		(
			[Id] [int] IDENTITY(1,1) NOT NULL,
			[TenantName] [nvarchar](256) NOT NULL,
			[StoreCode] [nvarchar](64) NOT NULL,
			[EnableTagging] [bit] NOT NULL,
			[TagPrefix] [nvarchar](16) NULL,
			[NextTagNumber] [int] NOT NULL,
			[TagNumberPadding] [int] NOT NULL,
			[ResetTagNumberYearly] [bit] NOT NULL,
			[Notes] [nvarchar](300) NULL,
			[CreatedDate] [datetime2](7) NOT NULL,
			[ModifiedDate] [datetime2](7) NOT NULL,
			CONSTRAINT [PK_BarcodeTagSettings] PRIMARY KEY CLUSTERED ([Id] ASC)
		);
	END;

	IF NOT EXISTS
	(
		SELECT 1
		FROM sys.indexes
		WHERE name = N'IX_LaundryOrderItems_OrderNo'
		  AND object_id = OBJECT_ID(N'[dbo].[LaundryOrderItems]')
	)
	BEGIN
		CREATE NONCLUSTERED INDEX [IX_LaundryOrderItems_OrderNo]
			ON [dbo].[LaundryOrderItems] ([OrderNo]);
	END;

	IF NOT EXISTS
	(
		SELECT 1
		FROM sys.indexes
		WHERE name = N'IX_BarcodeTagSettings_TenantName_StoreCode'
		  AND object_id = OBJECT_ID(N'[dbo].[BarcodeTagSettings]')
	)
	BEGIN
		CREATE UNIQUE NONCLUSTERED INDEX [IX_BarcodeTagSettings_TenantName_StoreCode]
			ON [dbo].[BarcodeTagSettings] ([TenantName], [StoreCode]);
	END;

	IF EXISTS
	(
		SELECT 1
		FROM sys.indexes
		WHERE name = N'IX_LaundryOrderItems_TenantName_StoreCode_TagNo'
		  AND object_id = OBJECT_ID(N'[dbo].[LaundryOrderItems]')
		  AND (
				is_unique = 0
				OR filter_definition IS NULL
				OR filter_definition NOT LIKE '%[TagNo] IS NOT NULL%'
			  )
	)
	BEGIN
		DROP INDEX [IX_LaundryOrderItems_TenantName_StoreCode_TagNo] ON [dbo].[LaundryOrderItems];
	END;

	IF NOT EXISTS
	(
		SELECT 1
		FROM sys.indexes
		WHERE name = N'IX_LaundryOrderItems_TenantName_StoreCode_TagNo'
		  AND object_id = OBJECT_ID(N'[dbo].[LaundryOrderItems]')
	)
	BEGIN
		IF EXISTS
		(
			SELECT 1
			FROM [dbo].[LaundryOrderItems]
			WHERE [TagNo] IS NOT NULL
			GROUP BY [TenantName], [StoreCode], [TagNo]
			HAVING COUNT(1) > 1
		)
		BEGIN
			THROW 52001, 'Duplicate non-null TagNo values detected per TenantName/StoreCode in dbo.LaundryOrderItems. Resolve duplicates before creating IX_LaundryOrderItems_TenantName_StoreCode_TagNo.', 1;
		END;

		CREATE UNIQUE NONCLUSTERED INDEX [IX_LaundryOrderItems_TenantName_StoreCode_TagNo]
			ON [dbo].[LaundryOrderItems] ([TenantName], [StoreCode], [TagNo])
			WHERE [TagNo] IS NOT NULL;
	END;

	COMMIT TRAN;
END TRY
BEGIN CATCH
	IF @@TRANCOUNT > 0
		ROLLBACK TRAN;

	DECLARE @ErrMsg nvarchar(4000) = ERROR_MESSAGE();
	DECLARE @ErrNo int = ERROR_NUMBER();
	DECLARE @ErrState int = ERROR_STATE();

	THROW @ErrNo, @ErrMsg, @ErrState;
END CATCH;
GO

/*
Read-only duplicate report query (run separately if needed):

SELECT
	TenantName,
	StoreCode,
	TagNo,
	COUNT(1) AS DuplicateCount
FROM dbo.LaundryOrderItems
WHERE TagNo IS NOT NULL
GROUP BY TenantName, StoreCode, TagNo
HAVING COUNT(1) > 1
ORDER BY DuplicateCount DESC, TenantName, StoreCode, TagNo;
*/
