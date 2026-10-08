using LMS.Identity.BusinessSerive.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Identity.BusinessSerive.Data.Migrations
{
    [DbContext(typeof(IdentityDbContext))]
    [Migration("20261015100000_RepairSuperAdminDefaults")]
    public partial class RepairSuperAdminDefaults : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DECLARE @constraintName sysname;
DECLARE @definition nvarchar(max);
DECLARE @dropSql nvarchar(max);

SELECT @constraintName = dc.name, @definition = dc.definition
FROM sys.default_constraints AS dc
JOIN sys.columns AS c
    ON c.object_id = dc.parent_object_id
    AND c.column_id = dc.parent_column_id
WHERE dc.parent_object_id = OBJECT_ID(N'[dbo].[SuperAdmins]')
  AND c.name = N'IsActive';

IF @constraintName IS NULL OR LOWER(REPLACE(REPLACE(REPLACE(@definition, '(', ''), ')', ''), ' ', '')) <> N'1'
BEGIN
    IF @constraintName IS NOT NULL
    BEGIN
        SET @dropSql = N'ALTER TABLE [dbo].[SuperAdmins] DROP CONSTRAINT ' + QUOTENAME(@constraintName);
        EXEC sys.sp_executesql @dropSql;
    END;

    ALTER TABLE [dbo].[SuperAdmins]
        ADD CONSTRAINT [DF_SuperAdmins_IsActive] DEFAULT (1) FOR [IsActive];
END;

SET @constraintName = NULL;
SET @definition = NULL;

SELECT @constraintName = dc.name, @definition = dc.definition
FROM sys.default_constraints AS dc
JOIN sys.columns AS c
    ON c.object_id = dc.parent_object_id
    AND c.column_id = dc.parent_column_id
WHERE dc.parent_object_id = OBJECT_ID(N'[dbo].[SuperAdmins]')
  AND c.name = N'IsPasswordChanged';

IF @constraintName IS NULL OR LOWER(REPLACE(REPLACE(REPLACE(@definition, '(', ''), ')', ''), ' ', '')) <> N'0'
BEGIN
    IF @constraintName IS NOT NULL
    BEGIN
        SET @dropSql = N'ALTER TABLE [dbo].[SuperAdmins] DROP CONSTRAINT ' + QUOTENAME(@constraintName);
        EXEC sys.sp_executesql @dropSql;
    END;

    ALTER TABLE [dbo].[SuperAdmins]
        ADD CONSTRAINT [DF_SuperAdmins_IsPasswordChanged] DEFAULT (0) FOR [IsPasswordChanged];
END;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[SuperAdmins]', N'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1 FROM sys.default_constraints
        WHERE parent_object_id = OBJECT_ID(N'[dbo].[SuperAdmins]')
          AND name = N'DF_SuperAdmins_IsActive')
        ALTER TABLE [dbo].[SuperAdmins] DROP CONSTRAINT [DF_SuperAdmins_IsActive];

    IF EXISTS (
        SELECT 1 FROM sys.default_constraints
        WHERE parent_object_id = OBJECT_ID(N'[dbo].[SuperAdmins]')
          AND name = N'DF_SuperAdmins_IsPasswordChanged')
        ALTER TABLE [dbo].[SuperAdmins] DROP CONSTRAINT [DF_SuperAdmins_IsPasswordChanged];
END;");
        }
    }
}
