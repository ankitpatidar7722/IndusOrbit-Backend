-- Add the "Email" (Gmail-like client) module to the DB-driven sidebar.
-- Clones an existing module's ModuleMaster row + all its per-user grants so it
-- inherits whatever grouping/company/permission conventions the DB already uses.
-- Idempotent: safe to re-run. Route = /email, icon = Mail.
SET NOCOUNT ON;

-- Template module to clone from (Customers if present, else the first live module).
DECLARE @tid bigint = (SELECT TOP 1 ModuleID FROM app.ModuleMaster WHERE ReactRoutePath = '/customers' AND ISNULL(IsDeletedTransaction,0)=0);
IF @tid IS NULL SET @tid = (SELECT TOP 1 ModuleID FROM app.ModuleMaster WHERE ISNULL(IsDeletedTransaction,0)=0 ORDER BY ModuleID);

DECLARE @nid bigint = (SELECT TOP 1 ModuleID FROM app.ModuleMaster WHERE ReactRoutePath = '/email');
IF @nid IS NULL
BEGIN
    SET @nid = (SELECT ISNULL(MAX(ModuleID),0)+1 FROM app.ModuleMaster);
    INSERT INTO app.ModuleMaster
      (ModuleID, ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
       ModuleDisplayOrder, SetGroupIndex, ModuleIcon, ParentModuleName, ReactRoutePath, CompanyID, IsDeletedTransaction)
    SELECT @nid, 'Email', 'Email', ModuleHeadName, ModuleHeadDisplayName,
       ModuleDisplayOrder, SetGroupIndex, 'Mail', ParentModuleName, '/email', CompanyID, 0
    FROM app.ModuleMaster WHERE ModuleID = @tid;
END

-- Clone grants: copy every UserModuleAuthentication row for the template module to the
-- new module id (dynamic — adapts to whatever columns the table actually has, minus identity).
DECLARE @cols nvarchar(max) = (
    SELECT STRING_AGG(QUOTENAME(c.name), ',') WITHIN GROUP (ORDER BY c.column_id)
    FROM sys.columns c
    WHERE c.object_id = OBJECT_ID('app.UserModuleAuthentication')
      AND c.is_identity = 0 AND c.is_computed = 0);

DECLARE @sql nvarchar(max) =
    'INSERT INTO app.UserModuleAuthentication (' + @cols + ') ' +
    'SELECT ' + REPLACE(@cols, '[ModuleID]', CAST(@nid AS varchar(20))) + ' ' +
    'FROM app.UserModuleAuthentication src ' +
    'WHERE src.ModuleID = ' + CAST(@tid AS varchar(20)) + ' ' +
    '  AND NOT EXISTS (SELECT 1 FROM app.UserModuleAuthentication d WHERE d.UserId = src.UserId AND d.ModuleID = ' + CAST(@nid AS varchar(20)) + ');';
EXEC sp_executesql @sql;

SELECT 'Email module id = ' + CAST(@nid AS varchar(20)) + ', grants cloned = ' + CAST(@@ROWCOUNT AS varchar(20)) AS Result;
