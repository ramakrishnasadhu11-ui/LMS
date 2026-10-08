SET NOCOUNT ON;

DECLARE @TargetDb sysname = N'$(TargetDb)';

DECLARE @SourceDbs TABLE (DbName sysname NOT NULL);
INSERT INTO @SourceDbs (DbName)
VALUES (N'$(IdentityDb)'), (N'$(MasterDb)'), (N'$(UiDb)');

IF DB_ID(@TargetDb) IS NULL
BEGIN
	RAISERROR('Target database %s was not found.', 16, 1, @TargetDb);
	RETURN;
END;

IF OBJECT_ID('tempdb..#SourceTables') IS NOT NULL DROP TABLE #SourceTables;
CREATE TABLE #SourceTables
(
	SourceDb sysname NOT NULL,
	SchemaName sysname NOT NULL,
	TableName sysname NOT NULL
);

DECLARE @SourceDb sysname;
DECLARE source_cursor CURSOR FAST_FORWARD FOR
SELECT DbName
FROM @SourceDbs
WHERE DB_ID(DbName) IS NOT NULL
  AND DbName <> @TargetDb;

OPEN source_cursor;
FETCH NEXT FROM source_cursor INTO @SourceDb;

WHILE @@FETCH_STATUS = 0
BEGIN
	DECLARE @sql nvarchar(max) = N'
		INSERT INTO #SourceTables (SourceDb, SchemaName, TableName)
		SELECT @pSourceDb, s.name, t.name
		FROM ' + QUOTENAME(@SourceDb) + N'.sys.tables t
		JOIN ' + QUOTENAME(@SourceDb) + N'.sys.schemas s ON s.schema_id = t.schema_id
		WHERE t.is_ms_shipped = 0;';

	EXEC sp_executesql @sql, N'@pSourceDb sysname', @pSourceDb = @SourceDb;
	FETCH NEXT FROM source_cursor INTO @SourceDb;
END;

CLOSE source_cursor;
DEALLOCATE source_cursor;

DECLARE @SchemaName sysname;
DECLARE @TableName sysname;

DECLARE table_cursor CURSOR FAST_FORWARD FOR
SELECT SourceDb, SchemaName, TableName
FROM #SourceTables
ORDER BY SourceDb, SchemaName, TableName;

OPEN table_cursor;
FETCH NEXT FROM table_cursor INTO @SourceDb, @SchemaName, @TableName;

WHILE @@FETCH_STATUS = 0
BEGIN
	BEGIN TRY
		DECLARE @TargetTable nvarchar(512) = QUOTENAME(@TargetDb) + N'.' + QUOTENAME(@SchemaName) + N'.' + QUOTENAME(@TableName);
		DECLARE @SourceTable nvarchar(512) = QUOTENAME(@SourceDb) + N'.' + QUOTENAME(@SchemaName) + N'.' + QUOTENAME(@TableName);

		DECLARE @CreateSchemaSql nvarchar(max) = N'
			IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@TargetDb) + N'.sys.schemas WHERE name = @pSchema)
			EXEC(''CREATE SCHEMA ' + QUOTENAME(@SchemaName) + N''');';
		EXEC sp_executesql @CreateSchemaSql, N'@pSchema sysname', @pSchema = @SchemaName;

		DECLARE @TargetExists bit = 0;
		DECLARE @CheckTargetSql nvarchar(max) = N'
			SELECT @pExists = CASE WHEN EXISTS (
				SELECT 1
				FROM ' + QUOTENAME(@TargetDb) + N'.sys.tables t
				JOIN ' + QUOTENAME(@TargetDb) + N'.sys.schemas s ON s.schema_id = t.schema_id
				WHERE s.name = @pSchema AND t.name = @pTable
			) THEN 1 ELSE 0 END;';
		EXEC sp_executesql
			@CheckTargetSql,
			N'@pSchema sysname, @pTable sysname, @pExists bit OUTPUT',
			@pSchema = @SchemaName,
			@pTable = @TableName,
			@pExists = @TargetExists OUTPUT;

		IF @TargetExists = 0
		BEGIN
			DECLARE @CreateTableSql nvarchar(max) = N'SELECT TOP (0) * INTO ' + @TargetTable + N' FROM ' + @SourceTable + N';';
			EXEC (@CreateTableSql);
			PRINT 'Created table: ' + @TargetTable;
		END;

		DECLARE @ColumnList nvarchar(max);
		DECLARE @ColumnsSql nvarchar(max) = N'
			SELECT @pColumns = STRING_AGG(QUOTENAME(tc.name), '','')
			FROM ' + QUOTENAME(@TargetDb) + N'.sys.columns tc
			JOIN ' + QUOTENAME(@TargetDb) + N'.sys.tables tt ON tt.object_id = tc.object_id
			JOIN ' + QUOTENAME(@TargetDb) + N'.sys.schemas ts ON ts.schema_id = tt.schema_id
			JOIN ' + QUOTENAME(@SourceDb) + N'.sys.columns sc ON sc.name = tc.name
			JOIN ' + QUOTENAME(@SourceDb) + N'.sys.tables st ON st.object_id = sc.object_id
			JOIN ' + QUOTENAME(@SourceDb) + N'.sys.schemas ss ON ss.schema_id = st.schema_id
			WHERE ts.name = @pSchema AND tt.name = @pTable
			  AND ss.name = @pSchema AND st.name = @pTable
			  AND tc.is_computed = 0 AND sc.is_computed = 0;';

		EXEC sp_executesql
			@ColumnsSql,
			N'@pSchema sysname, @pTable sysname, @pColumns nvarchar(max) OUTPUT',
			@pSchema = @SchemaName,
			@pTable = @TableName,
			@pColumns = @ColumnList OUTPUT;

		IF @ColumnList IS NULL OR LEN(@ColumnList) = 0
		BEGIN
			PRINT 'Skipped (no shared insertable columns): ' + @SourceTable;
			FETCH NEXT FROM table_cursor INTO @SourceDb, @SchemaName, @TableName;
			CONTINUE;
		END;

		DECLARE @PkJoin nvarchar(max);
		DECLARE @PkSql nvarchar(max) = N'
			SELECT @pPkJoin = STRING_AGG(''t.'' + QUOTENAME(c.name) + '' = s.'' + QUOTENAME(c.name), '' AND '')
			FROM ' + QUOTENAME(@TargetDb) + N'.sys.indexes i
			JOIN ' + QUOTENAME(@TargetDb) + N'.sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
			JOIN ' + QUOTENAME(@TargetDb) + N'.sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
			JOIN ' + QUOTENAME(@TargetDb) + N'.sys.tables t ON t.object_id = i.object_id
			JOIN ' + QUOTENAME(@TargetDb) + N'.sys.schemas s ON s.schema_id = t.schema_id
			WHERE i.is_primary_key = 1
			  AND s.name = @pSchema
			  AND t.name = @pTable;';

		EXEC sp_executesql
			@PkSql,
			N'@pSchema sysname, @pTable sysname, @pPkJoin nvarchar(max) OUTPUT',
			@pSchema = @SchemaName,
			@pTable = @TableName,
			@pPkJoin = @PkJoin OUTPUT;

		DECLARE @HasIdentityInInsert bit = 0;
		DECLARE @IdentityInInsertSql nvarchar(max) = N'
			SELECT @pHasIdentity = CASE WHEN EXISTS (
				SELECT 1
				FROM ' + QUOTENAME(@TargetDb) + N'.sys.columns tc
				JOIN ' + QUOTENAME(@TargetDb) + N'.sys.tables tt ON tt.object_id = tc.object_id
				JOIN ' + QUOTENAME(@TargetDb) + N'.sys.schemas ts ON ts.schema_id = tt.schema_id
				JOIN ' + QUOTENAME(@SourceDb) + N'.sys.columns sc ON sc.name = tc.name
				JOIN ' + QUOTENAME(@SourceDb) + N'.sys.tables st ON st.object_id = sc.object_id
				JOIN ' + QUOTENAME(@SourceDb) + N'.sys.schemas ss ON ss.schema_id = st.schema_id
				WHERE ts.name = @pSchema
				  AND tt.name = @pTable
				  AND ss.name = @pSchema
				  AND st.name = @pTable
				  AND tc.is_computed = 0
				  AND sc.is_computed = 0
				  AND tc.is_identity = 1
			) THEN 1 ELSE 0 END;';

		EXEC sp_executesql
			@IdentityInInsertSql,
			N'@pSchema sysname, @pTable sysname, @pHasIdentity bit OUTPUT',
			@pSchema = @SchemaName,
			@pTable = @TableName,
			@pHasIdentity = @HasIdentityInInsert OUTPUT;

		DECLARE @WhereClause nvarchar(max);
		IF @PkJoin IS NOT NULL AND LEN(@PkJoin) > 0
			SET @WhereClause = N'NOT EXISTS (SELECT 1 FROM ' + @TargetTable + N' t WHERE ' + @PkJoin + N')';
		ELSE
			SET @WhereClause = N'NOT EXISTS (SELECT 1 FROM ' + @TargetTable + N')';

		DECLARE @InsertSql nvarchar(max) = N'
			INSERT INTO ' + @TargetTable + N' (' + @ColumnList + N')
			SELECT ' + @ColumnList + N'
			FROM ' + @SourceTable + N' s
			WHERE ' + @WhereClause + N';';

		IF @HasIdentityInInsert = 1
		BEGIN
			DECLARE @IdentityInsertSql nvarchar(max) = N'
				SET IDENTITY_INSERT ' + @TargetTable + N' ON;
				' + @InsertSql + N'
				SET IDENTITY_INSERT ' + @TargetTable + N' OFF;';
			EXEC (@IdentityInsertSql);
		END
		ELSE
		BEGIN
			EXEC (@InsertSql);
		END

		PRINT 'Merged data: ' + @SourceTable + ' -> ' + @TargetTable;
	END TRY
	BEGIN CATCH
		PRINT 'Failed table merge [' + ISNULL(@SourceDb, '') + '].[' + ISNULL(@SchemaName, '') + '].[' + ISNULL(@TableName, '') + '] : ' + ERROR_MESSAGE();
	END CATCH;

	FETCH NEXT FROM table_cursor INTO @SourceDb, @SchemaName, @TableName;
END;

CLOSE table_cursor;
DEALLOCATE table_cursor;

PRINT 'Consolidation completed.';
