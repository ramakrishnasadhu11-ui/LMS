/* ============================================================
   Top-up Services migration precheck (READ-ONLY)
   Target migration:   20261117054518_AddTopUpServiceDomain
   Expected previous:  20261105051342_AddLaundryOrderTaxLines
   Safety: SELECT/metadata inspection only (no DML/DDL)
   ============================================================ */

SET NOCOUNT ON;

/* ============================================================
   Shared state (read-only flags used by multiple sections)
   ============================================================ */
DECLARE @HistoryExists bit = CASE WHEN OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL THEN 1 ELSE 0 END;
DECLARE @PrevRecorded bit = 0;
DECLARE @TopUpRecorded bit = 0;

IF @HistoryExists = 1
BEGIN
	SELECT
		@PrevRecorded = MAX(CASE WHEN MigrationId = N'20261105051342_AddLaundryOrderTaxLines' THEN 1 ELSE 0 END),
		@TopUpRecorded = MAX(CASE WHEN MigrationId = N'20261117054518_AddTopUpServiceDomain' THEN 1 ELSE 0 END)
	FROM dbo.__EFMigrationsHistory;
END;

/* ---------- A. Migration history status ---------- */
SELECT
	HistoryTableExists = @HistoryExists,
	ExpectedPreviousMigrationRecorded = @PrevRecorded,
	TopUpMigrationRecorded = @TopUpRecorded,
	MigrationHistoryStatus =
		CASE
			WHEN @HistoryExists = 0 THEN N'FAIL: __EFMigrationsHistory missing'
			WHEN @PrevRecorded = 0 THEN N'FAIL: expected previous migration missing'
			ELSE N'PASS'
		END;

IF @HistoryExists = 1
BEGIN
	SELECT
		MigrationId,
		ProductVersion
	FROM dbo.__EFMigrationsHistory
	WHERE MigrationId IN (
		N'20261105051342_AddLaundryOrderTaxLines',
		N'20261117054518_AddTopUpServiceDomain'
	)
	ORDER BY MigrationId;
END
ELSE
BEGIN
	SELECT
		CAST(NULL AS nvarchar(150)) AS MigrationId,
		CAST(NULL AS nvarchar(32)) AS ProductVersion,
		N'__EFMigrationsHistory table not found' AS Info;
END;

/* ---------- B. Table existence ---------- */
WITH TablePresence AS (
	SELECT
		v.TableName,
		ExistsFlag = CASE WHEN t.object_id IS NULL THEN 0 ELSE 1 END
	FROM (VALUES
		(N'TopUpServiceMaster'),
		(N'TopUpServiceApplicability'),
		(N'LaundryOrderTopUps')
	) v(TableName)
	LEFT JOIN sys.tables t
		ON t.name = v.TableName
	   AND SCHEMA_NAME(t.schema_id) = N'dbo'
)
SELECT *
FROM TablePresence
ORDER BY TableName;

WITH TablePresence AS (
	SELECT
		v.TableName,
		ExistsFlag = CASE WHEN t.object_id IS NULL THEN 0 ELSE 1 END
	FROM (VALUES
		(N'TopUpServiceMaster'),
		(N'TopUpServiceApplicability'),
		(N'LaundryOrderTopUps')
	) v(TableName)
	LEFT JOIN sys.tables t
		ON t.name = v.TableName
	   AND SCHEMA_NAME(t.schema_id) = N'dbo'
)
SELECT
	TopUpTablesPresentCount = SUM(ExistsFlag),
	IsPartialPresence = CASE WHEN SUM(ExistsFlag) IN (1,2) THEN 1 ELSE 0 END,
	TableExistenceStatus =
		CASE
			WHEN SUM(ExistsFlag) = 0 THEN N'NONE'
			WHEN SUM(ExistsFlag) = 3 THEN N'ALL'
			ELSE N'PARTIAL'
		END
FROM TablePresence;

/* ---------- C. Column/type parity against migration+DbContext ---------- */
WITH ExpectedColumns AS (
	SELECT * FROM (VALUES
	-- TopUpServiceMaster
	(N'TopUpServiceMaster', N'Id', N'int', 4, 10, 0, 0),
	(N'TopUpServiceMaster', N'TenantName', N'nvarchar', 256, NULL, NULL, 0),
	(N'TopUpServiceMaster', N'StoreCode', N'nvarchar', 64, NULL, NULL, 0),
	(N'TopUpServiceMaster', N'TopUpName', N'nvarchar', 128, NULL, NULL, 0),
	(N'TopUpServiceMaster', N'ChargeType', N'nvarchar', 32, NULL, NULL, 0),
	(N'TopUpServiceMaster', N'UnitPrice', N'decimal', NULL, 18, 2, 0),
	(N'TopUpServiceMaster', N'Description', N'nvarchar', 256, NULL, NULL, 1),
	(N'TopUpServiceMaster', N'IsActive', N'bit', 1, NULL, NULL, 0),
	(N'TopUpServiceMaster', N'IsArchived', N'bit', 1, NULL, NULL, 0),
	(N'TopUpServiceMaster', N'CreatedDate', N'datetime2', 8, NULL, NULL, 0),
	(N'TopUpServiceMaster', N'ModifiedDate', N'datetime2', 8, NULL, NULL, 0),

	-- TopUpServiceApplicability
	(N'TopUpServiceApplicability', N'Id', N'int', 4, 10, 0, 0),
	(N'TopUpServiceApplicability', N'TopUpServiceMasterId', N'int', 4, 10, 0, 0),
	(N'TopUpServiceApplicability', N'TenantName', N'nvarchar', 256, NULL, NULL, 0),
	(N'TopUpServiceApplicability', N'StoreCode', N'nvarchar', 64, NULL, NULL, 0),
	(N'TopUpServiceApplicability', N'ServiceType', N'nvarchar', 64, NULL, NULL, 1),
	(N'TopUpServiceApplicability', N'Category', N'nvarchar', 64, NULL, NULL, 1),
	(N'TopUpServiceApplicability', N'ItemName', N'nvarchar', 128, NULL, NULL, 1),
	(N'TopUpServiceApplicability', N'IsActive', N'bit', 1, NULL, NULL, 0),
	(N'TopUpServiceApplicability', N'CreatedDate', N'datetime2', 8, NULL, NULL, 0),
	(N'TopUpServiceApplicability', N'ModifiedDate', N'datetime2', 8, NULL, NULL, 0),

	-- LaundryOrderTopUps
	(N'LaundryOrderTopUps', N'Id', N'int', 4, 10, 0, 0),
	(N'LaundryOrderTopUps', N'LaundryOrderId', N'int', 4, 10, 0, 0),
	(N'LaundryOrderTopUps', N'TenantName', N'nvarchar', 256, NULL, NULL, 0),
	(N'LaundryOrderTopUps', N'StoreCode', N'nvarchar', 64, NULL, NULL, 0),
	(N'LaundryOrderTopUps', N'OrderNo', N'nvarchar', 64, NULL, NULL, 0),
	(N'LaundryOrderTopUps', N'LineNo', N'int', 4, 10, 0, 0),
	(N'LaundryOrderTopUps', N'TopUpName', N'nvarchar', 128, NULL, NULL, 0),
	(N'LaundryOrderTopUps', N'ChargeType', N'nvarchar', 32, NULL, NULL, 0),
	(N'LaundryOrderTopUps', N'ServiceType', N'nvarchar', 64, NULL, NULL, 1),
	(N'LaundryOrderTopUps', N'Category', N'nvarchar', 64, NULL, NULL, 1),
	(N'LaundryOrderTopUps', N'ItemName', N'nvarchar', 128, NULL, NULL, 1),
	(N'LaundryOrderTopUps', N'PieceNo', N'int', 4, 10, 0, 0),
	(N'LaundryOrderTopUps', N'AppliedQuantity', N'decimal', NULL, 18, 3, 0),
	(N'LaundryOrderTopUps', N'UnitPriceSnapshot', N'decimal', NULL, 18, 2, 0),
	(N'LaundryOrderTopUps', N'LineAmountSnapshot', N'decimal', NULL, 18, 2, 0),
	(N'LaundryOrderTopUps', N'CreatedDate', N'datetime2', 8, NULL, NULL, 0)
	) x(TableName, ColumnName, ExpectedSqlType, ExpectedMaxLenChars, ExpectedPrecision, ExpectedScale, ExpectedNullable)
),
ActualColumns AS (
	SELECT
		t.name AS TableName,
		c.name AS ColumnName,
		ty.name AS ActualSqlType,
		CASE
			WHEN ty.name IN (N'nvarchar',N'nchar') THEN c.max_length/2
			WHEN ty.name IN (N'varchar',N'char',N'varbinary',N'binary') THEN c.max_length
			ELSE NULL
		END AS ActualMaxLenChars,
		c.precision AS ActualPrecision,
		c.scale AS ActualScale,
		c.is_nullable AS ActualNullable
	FROM sys.tables t
	JOIN sys.columns c ON c.object_id = t.object_id
	JOIN sys.types ty ON ty.user_type_id = c.user_type_id
	WHERE SCHEMA_NAME(t.schema_id)=N'dbo'
	  AND t.name IN (N'TopUpServiceMaster',N'TopUpServiceApplicability',N'LaundryOrderTopUps')
)
SELECT
	e.TableName,
	e.ColumnName,
	e.ExpectedSqlType,
	a.ActualSqlType,
	e.ExpectedMaxLenChars,
	a.ActualMaxLenChars,
	e.ExpectedPrecision,
	a.ActualPrecision,
	e.ExpectedScale,
	a.ActualScale,
	e.ExpectedNullable,
	a.ActualNullable,
	MatchFlag =
		CASE
			WHEN a.ColumnName IS NULL THEN 0
			WHEN e.ExpectedSqlType <> a.ActualSqlType THEN 0
			WHEN ISNULL(e.ExpectedMaxLenChars,-1) <> ISNULL(a.ActualMaxLenChars,-1) THEN 0
			WHEN ISNULL(e.ExpectedPrecision,-1) <> ISNULL(a.ActualPrecision,-1) THEN 0
			WHEN ISNULL(e.ExpectedScale,-1) <> ISNULL(a.ActualScale,-1) THEN 0
			WHEN e.ExpectedNullable <> a.ActualNullable THEN 0
			ELSE 1
		END
FROM ExpectedColumns e
LEFT JOIN ActualColumns a
	ON a.TableName = e.TableName
   AND a.ColumnName = e.ColumnName
ORDER BY e.TableName, e.ColumnName;

WITH ExpectedColumns AS (
	SELECT * FROM (VALUES
	(N'TopUpServiceMaster', N'Id', N'int', 4, 10, 0, 0),
	(N'TopUpServiceMaster', N'TenantName', N'nvarchar', 256, NULL, NULL, 0),
	(N'TopUpServiceMaster', N'StoreCode', N'nvarchar', 64, NULL, NULL, 0),
	(N'TopUpServiceMaster', N'TopUpName', N'nvarchar', 128, NULL, NULL, 0),
	(N'TopUpServiceMaster', N'ChargeType', N'nvarchar', 32, NULL, NULL, 0),
	(N'TopUpServiceMaster', N'UnitPrice', N'decimal', NULL, 18, 2, 0),
	(N'TopUpServiceMaster', N'Description', N'nvarchar', 256, NULL, NULL, 1),
	(N'TopUpServiceMaster', N'IsActive', N'bit', 1, NULL, NULL, 0),
	(N'TopUpServiceMaster', N'IsArchived', N'bit', 1, NULL, NULL, 0),
	(N'TopUpServiceMaster', N'CreatedDate', N'datetime2', 8, NULL, NULL, 0),
	(N'TopUpServiceMaster', N'ModifiedDate', N'datetime2', 8, NULL, NULL, 0),
	(N'TopUpServiceApplicability', N'Id', N'int', 4, 10, 0, 0),
	(N'TopUpServiceApplicability', N'TopUpServiceMasterId', N'int', 4, 10, 0, 0),
	(N'TopUpServiceApplicability', N'TenantName', N'nvarchar', 256, NULL, NULL, 0),
	(N'TopUpServiceApplicability', N'StoreCode', N'nvarchar', 64, NULL, NULL, 0),
	(N'TopUpServiceApplicability', N'ServiceType', N'nvarchar', 64, NULL, NULL, 1),
	(N'TopUpServiceApplicability', N'Category', N'nvarchar', 64, NULL, NULL, 1),
	(N'TopUpServiceApplicability', N'ItemName', N'nvarchar', 128, NULL, NULL, 1),
	(N'TopUpServiceApplicability', N'IsActive', N'bit', 1, NULL, NULL, 0),
	(N'TopUpServiceApplicability', N'CreatedDate', N'datetime2', 8, NULL, NULL, 0),
	(N'TopUpServiceApplicability', N'ModifiedDate', N'datetime2', 8, NULL, NULL, 0),
	(N'LaundryOrderTopUps', N'Id', N'int', 4, 10, 0, 0),
	(N'LaundryOrderTopUps', N'LaundryOrderId', N'int', 4, 10, 0, 0),
	(N'LaundryOrderTopUps', N'TenantName', N'nvarchar', 256, NULL, NULL, 0),
	(N'LaundryOrderTopUps', N'StoreCode', N'nvarchar', 64, NULL, NULL, 0),
	(N'LaundryOrderTopUps', N'OrderNo', N'nvarchar', 64, NULL, NULL, 0),
	(N'LaundryOrderTopUps', N'LineNo', N'int', 4, 10, 0, 0),
	(N'LaundryOrderTopUps', N'TopUpName', N'nvarchar', 128, NULL, NULL, 0),
	(N'LaundryOrderTopUps', N'ChargeType', N'nvarchar', 32, NULL, NULL, 0),
	(N'LaundryOrderTopUps', N'ServiceType', N'nvarchar', 64, NULL, NULL, 1),
	(N'LaundryOrderTopUps', N'Category', N'nvarchar', 64, NULL, NULL, 1),
	(N'LaundryOrderTopUps', N'ItemName', N'nvarchar', 128, NULL, NULL, 1),
	(N'LaundryOrderTopUps', N'PieceNo', N'int', 4, 10, 0, 0),
	(N'LaundryOrderTopUps', N'AppliedQuantity', N'decimal', NULL, 18, 3, 0),
	(N'LaundryOrderTopUps', N'UnitPriceSnapshot', N'decimal', NULL, 18, 2, 0),
	(N'LaundryOrderTopUps', N'LineAmountSnapshot', N'decimal', NULL, 18, 2, 0),
	(N'LaundryOrderTopUps', N'CreatedDate', N'datetime2', 8, NULL, NULL, 0)
	) x(TableName, ColumnName, ExpectedSqlType, ExpectedMaxLenChars, ExpectedPrecision, ExpectedScale, ExpectedNullable)
),
ActualColumns AS (
	SELECT
		t.name AS TableName,
		c.name AS ColumnName,
		ty.name AS ActualSqlType,
		CASE
			WHEN ty.name IN (N'nvarchar',N'nchar') THEN c.max_length/2
			WHEN ty.name IN (N'varchar',N'char',N'varbinary',N'binary') THEN c.max_length
			ELSE NULL
		END AS ActualMaxLenChars,
		c.precision AS ActualPrecision,
		c.scale AS ActualScale,
		c.is_nullable AS ActualNullable
	FROM sys.tables t
	JOIN sys.columns c ON c.object_id = t.object_id
	JOIN sys.types ty ON ty.user_type_id = c.user_type_id
	WHERE SCHEMA_NAME(t.schema_id)=N'dbo'
	  AND t.name IN (N'TopUpServiceMaster',N'TopUpServiceApplicability',N'LaundryOrderTopUps')
),
Parity AS (
	SELECT
		MatchFlag =
			CASE
				WHEN a.ColumnName IS NULL THEN 0
				WHEN e.ExpectedSqlType <> a.ActualSqlType THEN 0
				WHEN ISNULL(e.ExpectedMaxLenChars,-1) <> ISNULL(a.ActualMaxLenChars,-1) THEN 0
				WHEN ISNULL(e.ExpectedPrecision,-1) <> ISNULL(a.ActualPrecision,-1) THEN 0
				WHEN ISNULL(e.ExpectedScale,-1) <> ISNULL(a.ActualScale,-1) THEN 0
				WHEN e.ExpectedNullable <> a.ActualNullable THEN 0
				ELSE 1
			END
	FROM ExpectedColumns e
	LEFT JOIN ActualColumns a
		ON a.TableName = e.TableName
	   AND a.ColumnName = e.ColumnName
)
SELECT
	SchemaParityStatus = CASE WHEN MIN(MatchFlag) = 1 THEN N'PASS' ELSE N'FAIL' END
FROM Parity;

/* ---------- D. PK inspection + automated PASS/FAIL ---------- */
WITH ActualPK AS (
	SELECT
		TableName = t.name,
		PKName = kc.name,
		PKColumns = STRING_AGG(c.name, N',') WITHIN GROUP (ORDER BY ic.key_ordinal)
	FROM sys.tables t
	JOIN sys.key_constraints kc
		ON kc.parent_object_id = t.object_id
	   AND kc.type = 'PK'
	JOIN sys.index_columns ic
		ON ic.object_id = kc.parent_object_id
	   AND ic.index_id = kc.unique_index_id
	JOIN sys.columns c
		ON c.object_id = ic.object_id
	   AND c.column_id = ic.column_id
	WHERE SCHEMA_NAME(t.schema_id)=N'dbo'
	  AND t.name IN (N'TopUpServiceMaster',N'TopUpServiceApplicability',N'LaundryOrderTopUps')
	GROUP BY t.name, kc.name
),
ExpectedPK AS (
	SELECT * FROM (VALUES
		(N'TopUpServiceMaster', N'PK_TopUpServiceMaster', N'Id'),
		(N'TopUpServiceApplicability', N'PK_TopUpServiceApplicability', N'Id'),
		(N'LaundryOrderTopUps', N'PK_LaundryOrderTopUps', N'Id')
	) x(TableName, PKName, PKColumns)
)
SELECT
	e.TableName,
	e.PKName AS ExpectedPKName,
	e.PKColumns AS ExpectedPKColumns,
	a.PKName AS ActualPKName,
	a.PKColumns AS ActualPKColumns,
	CompatibleFlag =
		CASE WHEN a.TableName IS NULL THEN 0
			 WHEN e.PKName <> a.PKName THEN 0
			 WHEN e.PKColumns <> a.PKColumns THEN 0
			 ELSE 1 END
FROM ExpectedPK e
LEFT JOIN ActualPK a
	ON a.TableName = e.TableName
ORDER BY e.TableName;

WITH ActualPK AS (
	SELECT
		TableName = t.name,
		PKName = kc.name,
		PKColumns = STRING_AGG(c.name, N',') WITHIN GROUP (ORDER BY ic.key_ordinal)
	FROM sys.tables t
	JOIN sys.key_constraints kc
		ON kc.parent_object_id = t.object_id
	   AND kc.type = 'PK'
	JOIN sys.index_columns ic
		ON ic.object_id = kc.parent_object_id
	   AND ic.index_id = kc.unique_index_id
	JOIN sys.columns c
		ON c.object_id = ic.object_id
	   AND c.column_id = ic.column_id
	WHERE SCHEMA_NAME(t.schema_id)=N'dbo'
	  AND t.name IN (N'TopUpServiceMaster',N'TopUpServiceApplicability',N'LaundryOrderTopUps')
	GROUP BY t.name, kc.name
),
ExpectedPK AS (
	SELECT * FROM (VALUES
		(N'TopUpServiceMaster', N'PK_TopUpServiceMaster', N'Id'),
		(N'TopUpServiceApplicability', N'PK_TopUpServiceApplicability', N'Id'),
		(N'LaundryOrderTopUps', N'PK_LaundryOrderTopUps', N'Id')
	) x(TableName, PKName, PKColumns)
),
PKCheck AS (
	SELECT
		CompatibleFlag =
			CASE WHEN a.TableName IS NULL THEN 0
				 WHEN e.PKName <> a.PKName THEN 0
				 WHEN e.PKColumns <> a.PKColumns THEN 0
				 ELSE 1 END
	FROM ExpectedPK e
	LEFT JOIN ActualPK a
		ON a.TableName = e.TableName
)
SELECT
	PKCompatibilityStatus = CASE WHEN MIN(CompatibleFlag) = 1 THEN N'PASS' ELSE N'FAIL' END
FROM PKCheck;

/* ---------- E. FK inspection + automated PASS/FAIL ---------- */
WITH ActualFK AS (
	SELECT
		FKName = fk.name,
		ParentTable = tp.name,
		ParentColumn = cp.name,
		RefTable = tr.name,
		RefColumn = cr.name
	FROM sys.foreign_keys fk
	JOIN sys.foreign_key_columns fkc
		ON fkc.constraint_object_id = fk.object_id
	JOIN sys.tables tp ON tp.object_id = fk.parent_object_id
	JOIN sys.columns cp ON cp.object_id = tp.object_id AND cp.column_id = fkc.parent_column_id
	JOIN sys.tables tr ON tr.object_id = fk.referenced_object_id
	JOIN sys.columns cr ON cr.object_id = tr.object_id AND cr.column_id = fkc.referenced_column_id
	WHERE SCHEMA_NAME(tp.schema_id)=N'dbo'
	  AND tp.name IN (N'TopUpServiceApplicability',N'LaundryOrderTopUps')
),
ExpectedFK AS (
	SELECT * FROM (VALUES
		(N'FK_TopUpServiceApplicability_TopUpServiceMaster_TopUpServiceMasterId', N'TopUpServiceApplicability', N'TopUpServiceMasterId', N'TopUpServiceMaster', N'Id'),
		(N'FK_LaundryOrderTopUps_LaundryOrders_LaundryOrderId', N'LaundryOrderTopUps', N'LaundryOrderId', N'LaundryOrders', N'Id')
	) x(FKName, ParentTable, ParentColumn, RefTable, RefColumn)
)
SELECT
	e.FKName AS ExpectedFKName,
	e.ParentTable AS ExpectedParentTable,
	e.ParentColumn AS ExpectedParentColumn,
	e.RefTable AS ExpectedRefTable,
	e.RefColumn AS ExpectedRefColumn,
	a.FKName AS ActualFKName,
	a.ParentTable AS ActualParentTable,
	a.ParentColumn AS ActualParentColumn,
	a.RefTable AS ActualRefTable,
	a.RefColumn AS ActualRefColumn,
	CompatibleFlag =
		CASE WHEN a.FKName IS NULL THEN 0
			 WHEN e.ParentTable <> a.ParentTable THEN 0
			 WHEN e.ParentColumn <> a.ParentColumn THEN 0
			 WHEN e.RefTable <> a.RefTable THEN 0
			 WHEN e.RefColumn <> a.RefColumn THEN 0
			 ELSE 1 END
FROM ExpectedFK e
LEFT JOIN ActualFK a
	ON a.FKName = e.FKName
ORDER BY e.FKName;

WITH ActualFK AS (
	SELECT
		FKName = fk.name,
		ParentTable = tp.name,
		ParentColumn = cp.name,
		RefTable = tr.name,
		RefColumn = cr.name
	FROM sys.foreign_keys fk
	JOIN sys.foreign_key_columns fkc
		ON fkc.constraint_object_id = fk.object_id
	JOIN sys.tables tp ON tp.object_id = fk.parent_object_id
	JOIN sys.columns cp ON cp.object_id = tp.object_id AND cp.column_id = fkc.parent_column_id
	JOIN sys.tables tr ON tr.object_id = fk.referenced_object_id
	JOIN sys.columns cr ON cr.object_id = tr.object_id AND cr.column_id = fkc.referenced_column_id
	WHERE SCHEMA_NAME(tp.schema_id)=N'dbo'
	  AND tp.name IN (N'TopUpServiceApplicability',N'LaundryOrderTopUps')
),
ExpectedFK AS (
	SELECT * FROM (VALUES
		(N'FK_TopUpServiceApplicability_TopUpServiceMaster_TopUpServiceMasterId', N'TopUpServiceApplicability', N'TopUpServiceMasterId', N'TopUpServiceMaster', N'Id'),
		(N'FK_LaundryOrderTopUps_LaundryOrders_LaundryOrderId', N'LaundryOrderTopUps', N'LaundryOrderId', N'LaundryOrders', N'Id')
	) x(FKName, ParentTable, ParentColumn, RefTable, RefColumn)
),
FKCheck AS (
	SELECT
		CompatibleFlag =
			CASE WHEN a.FKName IS NULL THEN 0
				 WHEN e.ParentTable <> a.ParentTable THEN 0
				 WHEN e.ParentColumn <> a.ParentColumn THEN 0
				 WHEN e.RefTable <> a.RefTable THEN 0
				 WHEN e.RefColumn <> a.RefColumn THEN 0
				 ELSE 1 END
	FROM ExpectedFK e
	LEFT JOIN ActualFK a
		ON a.FKName = e.FKName
)
SELECT
	FKCompatibilityStatus = CASE WHEN MIN(CompatibleFlag) = 1 THEN N'PASS' ELSE N'FAIL' END
FROM FKCheck;

/* ---------- F. Index inspection (names, keys, uniqueness, filter) ---------- */
WITH Ix AS (
	SELECT
		t.name AS TableName,
		i.name AS IndexName,
		i.is_unique,
		i.has_filter,
		i.filter_definition,
		KeyColumns = STRING_AGG(
			CASE WHEN ic.is_included_column = 0 THEN c.name END, N','
		) WITHIN GROUP (ORDER BY CASE WHEN ic.is_included_column = 0 THEN ic.key_ordinal END)
	FROM sys.tables t
	JOIN sys.indexes i
		ON i.object_id = t.object_id
	JOIN sys.index_columns ic
		ON ic.object_id = i.object_id
	   AND ic.index_id = i.index_id
	JOIN sys.columns c
		ON c.object_id = ic.object_id
	   AND c.column_id = ic.column_id
	WHERE SCHEMA_NAME(t.schema_id)=N'dbo'
	  AND t.name IN (N'TopUpServiceMaster',N'TopUpServiceApplicability',N'LaundryOrderTopUps')
	  AND i.type_desc <> N'HEAP'
	  AND i.is_hypothetical = 0
	GROUP BY t.name, i.name, i.is_unique, i.has_filter, i.filter_definition
)
SELECT *
FROM Ix
ORDER BY TableName, IndexName;

/* ---------- G. Explicit compatibility check against target migration indexes ---------- */
WITH ExpectedIdx AS (
	SELECT * FROM (VALUES
		(N'TopUpServiceMaster', N'IX_TopUpServiceMaster_TenantName_StoreCode_TopUpName', 1, N'TenantName,StoreCode,TopUpName', N'', 0),
		(N'TopUpServiceMaster', N'IX_TopUpServiceMaster_TenantName_StoreCode_IsActive_IsArchived', 0, N'TenantName,StoreCode,IsActive,IsArchived', N'', 0),
		(N'TopUpServiceApplicability', N'IX_TopUpServiceApplicability_TenantName_StoreCode_TopUpServiceMasterId_ServiceType_Category_ItemName', 1, N'TenantName,StoreCode,TopUpServiceMasterId,ServiceType,Category,ItemName', N'SERVICETYPEISNOTNULLANDCATEGORYISNOTNULLANDITEMNAMEISNOTNULL', 1),
		(N'TopUpServiceApplicability', N'IX_TopUpServiceApplicability_TenantName_StoreCode_IsActive', 0, N'TenantName,StoreCode,IsActive', N'', 0),
		(N'TopUpServiceApplicability', N'IX_TopUpServiceApplicability_TopUpServiceMasterId', 0, N'TopUpServiceMasterId', N'', 0),
		(N'LaundryOrderTopUps', N'IX_LaundryOrderTopUps_LaundryOrderId_LineNo', 0, N'LaundryOrderId,LineNo', N'', 0),
		(N'LaundryOrderTopUps', N'IX_LaundryOrderTopUps_TenantName_StoreCode_OrderNo_LineNo', 0, N'TenantName,StoreCode,OrderNo,LineNo', N'', 0)
	) x(TableName, IndexName, ExpectedUnique, ExpectedKeys, ExpectedFilterNorm, ExpectFiltered)
),
ActualIdx AS (
	SELECT
		t.name AS TableName,
		i.name AS IndexName,
		i.is_unique,
		i.has_filter,
		KeyColumns = STRING_AGG(CASE WHEN ic.is_included_column=0 THEN c.name END, N',')
			WITHIN GROUP (ORDER BY CASE WHEN ic.is_included_column=0 THEN ic.key_ordinal END),
		FilterNorm =
			UPPER(
				REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
					ISNULL(i.filter_definition,N''),
					N'[',N''), N']',N''), N'(',N''), N')',N''), N' ',N''),
					CHAR(9),N''), CHAR(10),N''), CHAR(13),N'')
			)
	FROM sys.tables t
	JOIN sys.indexes i ON i.object_id=t.object_id
	JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
	JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
	WHERE SCHEMA_NAME(t.schema_id)=N'dbo'
	  AND t.name IN (N'TopUpServiceMaster',N'TopUpServiceApplicability',N'LaundryOrderTopUps')
	  AND i.name IS NOT NULL
	GROUP BY t.name, i.name, i.is_unique, i.has_filter, i.filter_definition
)
SELECT
	e.TableName,
	e.IndexName,
	e.ExpectedUnique,
	e.ExpectedKeys,
	e.ExpectFiltered,
	e.ExpectedFilterNorm,
	a.is_unique AS ActualUnique,
	a.KeyColumns AS ActualKeys,
	a.has_filter AS ActualHasFilter,
	a.FilterNorm AS ActualFilterNorm,
	CompatibleFlag =
		CASE WHEN a.IndexName IS NULL THEN 0
			 WHEN e.ExpectedUnique <> a.is_unique THEN 0
			 WHEN e.ExpectedKeys <> a.KeyColumns THEN 0
			 WHEN e.ExpectFiltered <> a.has_filter THEN 0
			 WHEN ISNULL(e.ExpectedFilterNorm,N'') <> ISNULL(a.FilterNorm,N'') THEN 0
			 ELSE 1 END
FROM ExpectedIdx e
LEFT JOIN ActualIdx a
	ON a.TableName = e.TableName
   AND a.IndexName = e.IndexName
ORDER BY e.TableName, e.IndexName;

WITH ExpectedIdx AS (
	SELECT * FROM (VALUES
		(N'TopUpServiceMaster', N'IX_TopUpServiceMaster_TenantName_StoreCode_TopUpName', 1, N'TenantName,StoreCode,TopUpName', N'', 0),
		(N'TopUpServiceMaster', N'IX_TopUpServiceMaster_TenantName_StoreCode_IsActive_IsArchived', 0, N'TenantName,StoreCode,IsActive,IsArchived', N'', 0),
		(N'TopUpServiceApplicability', N'IX_TopUpServiceApplicability_TenantName_StoreCode_TopUpServiceMasterId_ServiceType_Category_ItemName', 1, N'TenantName,StoreCode,TopUpServiceMasterId,ServiceType,Category,ItemName', N'SERVICETYPEISNOTNULLANDCATEGORYISNOTNULLANDITEMNAMEISNOTNULL', 1),
		(N'TopUpServiceApplicability', N'IX_TopUpServiceApplicability_TenantName_StoreCode_IsActive', 0, N'TenantName,StoreCode,IsActive', N'', 0),
		(N'TopUpServiceApplicability', N'IX_TopUpServiceApplicability_TopUpServiceMasterId', 0, N'TopUpServiceMasterId', N'', 0),
		(N'LaundryOrderTopUps', N'IX_LaundryOrderTopUps_LaundryOrderId_LineNo', 0, N'LaundryOrderId,LineNo', N'', 0),
		(N'LaundryOrderTopUps', N'IX_LaundryOrderTopUps_TenantName_StoreCode_OrderNo_LineNo', 0, N'TenantName,StoreCode,OrderNo,LineNo', N'', 0)
	) x(TableName, IndexName, ExpectedUnique, ExpectedKeys, ExpectedFilterNorm, ExpectFiltered)
),
ActualIdx AS (
	SELECT
		t.name AS TableName,
		i.name AS IndexName,
		i.is_unique,
		i.has_filter,
		KeyColumns = STRING_AGG(CASE WHEN ic.is_included_column=0 THEN c.name END, N',')
			WITHIN GROUP (ORDER BY CASE WHEN ic.is_included_column=0 THEN ic.key_ordinal END),
		FilterNorm =
			UPPER(
				REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
					ISNULL(i.filter_definition,N''),
					N'[',N''), N']',N''), N'(',N''), N')',N''), N' ',N''),
					CHAR(9),N''), CHAR(10),N''), CHAR(13),N'')
			)
	FROM sys.tables t
	JOIN sys.indexes i ON i.object_id=t.object_id
	JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
	JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
	WHERE SCHEMA_NAME(t.schema_id)=N'dbo'
	  AND t.name IN (N'TopUpServiceMaster',N'TopUpServiceApplicability',N'LaundryOrderTopUps')
	  AND i.name IS NOT NULL
	GROUP BY t.name, i.name, i.is_unique, i.has_filter, i.filter_definition
),
IxCheck AS (
	SELECT
		CompatibleFlag =
			CASE WHEN a.IndexName IS NULL THEN 0
				 WHEN e.ExpectedUnique <> a.is_unique THEN 0
				 WHEN e.ExpectedKeys <> a.KeyColumns THEN 0
				 WHEN e.ExpectFiltered <> a.has_filter THEN 0
				 WHEN ISNULL(e.ExpectedFilterNorm,N'') <> ISNULL(a.FilterNorm,N'') THEN 0
				 ELSE 1 END
	FROM ExpectedIdx e
	LEFT JOIN ActualIdx a
		ON a.TableName = e.TableName
	   AND a.IndexName = e.IndexName
)
SELECT
	IndexCompatibilityStatus = CASE WHEN MIN(CompatibleFlag) = 1 THEN N'PASS' ELSE N'FAIL' END
FROM IxCheck;

/* ---------- H. Legacy-vs-new duplicate logical index names (runtime compatibility risk) ---------- */
SELECT
	TableName = t.name,
	i.name AS IndexName
FROM sys.tables t
JOIN sys.indexes i ON i.object_id=t.object_id
WHERE SCHEMA_NAME(t.schema_id)=N'dbo'
  AND t.name IN (N'TopUpServiceMaster',N'TopUpServiceApplicability',N'LaundryOrderTopUps')
  AND i.name IN (
	  N'IX_TopUpServiceMaster_Tenant_Store_TopUpName',
	  N'IX_TopUpServiceMaster_Tenant_Store_IsActive_IsArchived',
	  N'IX_TopUpServiceApplicability_Tenant_Store_Master_Service_Category_Item',
	  N'IX_TopUpServiceApplicability_Tenant_Store_IsActive',
	  N'IX_LaundryOrderTopUps_Tenant_Store_OrderNo_LineNo',
	  N'IX_TopUpServiceMaster_TenantName_StoreCode_TopUpName',
	  N'IX_TopUpServiceMaster_TenantName_StoreCode_IsActive_IsArchived',
	  N'IX_TopUpServiceApplicability_TenantName_StoreCode_TopUpServiceMasterId_ServiceType_Category_ItemName',
	  N'IX_TopUpServiceApplicability_TenantName_StoreCode_IsActive',
	  N'IX_LaundryOrderTopUps_TenantName_StoreCode_OrderNo_LineNo'
  )
ORDER BY t.name, i.name;

/* ---------- I. Duplicate applicability mappings that can block unique filtered index ---------- */
IF OBJECT_ID(N'dbo.TopUpServiceApplicability', N'U') IS NOT NULL
BEGIN
	SELECT
		TenantName,
		StoreCode,
		TopUpServiceMasterId,
		ServiceType,
		Category,
		ItemName,
		DuplicateCount = COUNT_BIG(*)
	FROM dbo.TopUpServiceApplicability
	WHERE ServiceType IS NOT NULL
	  AND Category IS NOT NULL
	  AND ItemName IS NOT NULL
	GROUP BY
		TenantName, StoreCode, TopUpServiceMasterId, ServiceType, Category, ItemName
	HAVING COUNT_BIG(*) > 1
	ORDER BY DuplicateCount DESC;
END
ELSE
BEGIN
	SELECT
		CAST(NULL AS nvarchar(256)) AS TenantName,
		CAST(NULL AS nvarchar(64)) AS StoreCode,
		CAST(NULL AS int) AS TopUpServiceMasterId,
		CAST(NULL AS nvarchar(64)) AS ServiceType,
		CAST(NULL AS nvarchar(64)) AS Category,
		CAST(NULL AS nvarchar(128)) AS ItemName,
		CAST(NULL AS bigint) AS DuplicateCount,
		N'TopUpServiceApplicability table missing (duplicate check cannot run)' AS Info;
END;

/* ---------- J. One-row summary / Path decision / blocking risks ---------- */
WITH TablePresence AS (
	SELECT
		v.TableName,
		ExistsFlag = CASE WHEN t.object_id IS NULL THEN 0 ELSE 1 END
	FROM (VALUES
		(N'TopUpServiceMaster'),
		(N'TopUpServiceApplicability'),
		(N'LaundryOrderTopUps')
	) v(TableName)
	LEFT JOIN sys.tables t
		ON t.name = v.TableName
	   AND SCHEMA_NAME(t.schema_id) = N'dbo'
),
PresenceAgg AS (
	SELECT
		PresentCount = SUM(ExistsFlag),
		AnyTableExists = CASE WHEN SUM(ExistsFlag) > 0 THEN 1 ELSE 0 END,
		IsPartial = CASE WHEN SUM(ExistsFlag) IN (1,2) THEN 1 ELSE 0 END,
		AllExpectedTablesExist = CASE WHEN SUM(ExistsFlag) = 3 THEN 1 ELSE 0 END,
		NoExpectedTablesExist = CASE WHEN SUM(ExistsFlag) = 0 THEN 1 ELSE 0 END
	FROM TablePresence
)
SELECT
	MigrationHistoryStatus =
		CASE
			WHEN @HistoryExists = 0 THEN N'FAIL: __EFMigrationsHistory missing'
			WHEN @PrevRecorded = 0 THEN N'FAIL: expected previous migration missing'
			WHEN @TopUpRecorded = 1 THEN N'PASS: top-up migration recorded'
			ELSE N'PASS: top-up migration not yet recorded'
		END,
	SchemaReadinessStatus =
		CASE
			WHEN p.IsPartial = 1 THEN N'FAIL: partial top-up table presence'
			WHEN @TopUpRecorded = 1 AND p.AllExpectedTablesExist = 0 THEN N'FAIL: migration recorded but expected tables do not fully exist'
			ELSE N'REVIEW_REQUIRED'
		END,
	TableExistenceStatus =
		CONCAT(N'Present=', p.PresentCount, N'/3; Partial=', p.IsPartial),
	PathDecision =
		CASE
			WHEN @HistoryExists = 0 THEN N'Blocked'
			WHEN @PrevRecorded = 0 THEN N'Blocked'
			WHEN p.IsPartial = 1 THEN N'Blocked'
			WHEN @TopUpRecorded = 1 AND p.AllExpectedTablesExist = 1 THEN N'AlreadyApplied'
			WHEN @TopUpRecorded = 1 AND p.AllExpectedTablesExist = 0 THEN N'Blocked'
			WHEN @TopUpRecorded = 0 AND p.NoExpectedTablesExist = 1 THEN N'Path 1'
			WHEN @TopUpRecorded = 0 AND p.AllExpectedTablesExist = 1 THEN N'Path 2'
			ELSE N'Blocked'
		END,
	ProvisionalDecisionNote =
		N'Path 1/Path 2 are provisional until Sections C, D, E, G, and I are reviewed.',
	BlockingRisks =
		CONCAT(
			CASE WHEN @HistoryExists = 0 THEN N'[Missing __EFMigrationsHistory] ' ELSE N'' END,
			CASE WHEN @HistoryExists = 1 AND @PrevRecorded = 0 THEN N'[Missing expected previous migration] ' ELSE N'' END,
			CASE WHEN p.IsPartial = 1 THEN N'[Partial top-up table presence] ' ELSE N'' END,
			CASE WHEN @TopUpRecorded = 1 AND p.AllExpectedTablesExist = 0 THEN N'[Recorded migration disagrees with physical schema] ' ELSE N'' END,
			N'[Review Sections C/D/E/G/I before declaring schema ready]'
		)
FROM PresenceAgg p;
