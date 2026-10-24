SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'[dbo].[LaundryOrderItems]', N'U') IS NULL
BEGIN
	CREATE TABLE [dbo].[LaundryOrderItems](
		[Id] [int] IDENTITY(1,1) NOT NULL,
		[TenantName] [nvarchar](256) NOT NULL,
		[StoreCode] [nvarchar](64) NOT NULL,
		[OrderNo] [nvarchar](64) NOT NULL,
		[ServiceType] [nvarchar](64) NULL,
		[Category] [nvarchar](64) NULL,
		[ItemName] [nvarchar](128) NULL,
		[UnitPrice] [decimal](18,2) NOT NULL CONSTRAINT [DF_LaundryOrderItems_UnitPrice] DEFAULT((0)),
		[PieceNo] [int] NOT NULL CONSTRAINT [DF_LaundryOrderItems_PieceNo] DEFAULT((1)),
		[TagNo] [nvarchar](32) NULL,
		[CreatedDate] [datetime2](7) NOT NULL,
		CONSTRAINT [PK_LaundryOrderItems] PRIMARY KEY CLUSTERED ([Id] ASC)
	);
END;

IF NOT EXISTS (
	SELECT 1
	FROM sys.indexes
	WHERE name = N'IX_LaundryOrderItems_OrderNo'
	  AND object_id = OBJECT_ID(N'[dbo].[LaundryOrderItems]')
)
BEGIN
	CREATE NONCLUSTERED INDEX [IX_LaundryOrderItems_OrderNo]
	ON [dbo].[LaundryOrderItems] ([OrderNo]);
END;

IF NOT EXISTS (
	SELECT 1
	FROM sys.indexes
	WHERE name = N'IX_LaundryOrderItems_TenantName_StoreCode_OrderNo'
	  AND object_id = OBJECT_ID(N'[dbo].[LaundryOrderItems]')
)
BEGIN
	CREATE NONCLUSTERED INDEX [IX_LaundryOrderItems_TenantName_StoreCode_OrderNo]
	ON [dbo].[LaundryOrderItems] ([TenantName], [StoreCode], [OrderNo]);
END;

IF EXISTS (
	SELECT 1
	FROM sys.indexes
	WHERE name = N'IX_LaundryOrderItems_TenantName_StoreCode_TagNo'
	  AND object_id = OBJECT_ID(N'[dbo].[LaundryOrderItems]')
	  AND (is_unique = 0 OR filter_definition IS NULL OR filter_definition NOT LIKE '%[TagNo] IS NOT NULL%')
)
BEGIN
	DROP INDEX [IX_LaundryOrderItems_TenantName_StoreCode_TagNo] ON [dbo].[LaundryOrderItems];
END;

IF NOT EXISTS (
	SELECT 1
	FROM sys.indexes
	WHERE name = N'IX_LaundryOrderItems_TenantName_StoreCode_TagNo'
	  AND object_id = OBJECT_ID(N'[dbo].[LaundryOrderItems]')
)
BEGIN
	CREATE UNIQUE NONCLUSTERED INDEX [IX_LaundryOrderItems_TenantName_StoreCode_TagNo]
	ON [dbo].[LaundryOrderItems] ([TenantName], [StoreCode], [TagNo])
	WHERE [TagNo] IS NOT NULL;
END;

IF OBJECT_ID(N'[dbo].[LaundryOrders]', N'U') IS NOT NULL
AND OBJECT_ID(N'[dbo].[FK_LaundryOrderItems_LaundryOrders_OrderNo]', N'F') IS NULL
BEGIN
	ALTER TABLE [dbo].[LaundryOrderItems] WITH CHECK
	ADD CONSTRAINT [FK_LaundryOrderItems_LaundryOrders_OrderNo]
		FOREIGN KEY ([OrderNo]) REFERENCES [dbo].[LaundryOrders]([OrderNo]);
END;

COMMIT TRANSACTION;
