-- Add the "Module Group Authority" management page to the DB-driven sidebar.
-- Convention in this DB: app.ModuleMaster.ModuleName holds the ROUTE (e.g. '/customers'),
-- ModuleDisplayName is the label, IsSidebarItem=1, ReactRoutePath is unused (NULL).
-- Clones an existing module's ModuleMaster row + its per-user grants. Idempotent.
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Template module to clone from (Customers if present, else the first sidebar module).
DECLARE @tid bigint = (SELECT TOP 1 ModuleID FROM app.ModuleMaster WHERE ModuleName = '/customers' AND ISNULL(IsDeletedTransaction,0)=0);
IF @tid IS NULL SET @tid = (SELECT TOP 1 ModuleID FROM app.ModuleMaster WHERE ISNULL(IsSidebarItem,0)=1 AND ISNULL(IsDeletedTransaction,0)=0 ORDER BY ModuleID);

-- Remove any partial/orphaned rows from a previous run so this is clean + idempotent.
DECLARE @old bigint = (SELECT TOP 1 ModuleID FROM app.ModuleMaster WHERE ModuleName = '/module-group-authority');
IF @old IS NOT NULL DELETE FROM app.UserModuleAuthentication WHERE ModuleID = @old;
-- (also clear grants left behind if the ModuleMaster row failed to insert last time)
DELETE FROM app.UserModuleAuthentication
 WHERE ModuleID NOT IN (SELECT ModuleID FROM app.ModuleMaster)
   AND ModuleID = 37;

DECLARE @nid bigint = @old;
IF @nid IS NULL
BEGIN
    -- ModuleID is an IDENTITY column — let SQL Server assign it, then capture it.
    -- STANDALONE top-level module: its own head (head == display) + unique SetGroupIndex,
    -- so it renders as its own sidebar entry (NOT nested under the Customers group).
    INSERT INTO app.ModuleMaster
      (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
       ModuleDisplayOrder, SetGroupIndex, ModuleIcon, ParentModuleName, CompanyID, IsSidebarItem, IsDeletedTransaction)
    SELECT '/module-group-authority', 'Module Group Authority', 'Module Group Authority', 'Module Group Authority',
       1.0, 2.55, 'Layers', NULL, CompanyID, 1, 0
    FROM app.ModuleMaster WHERE ModuleID = @tid;
    SET @nid = CAST(SCOPE_IDENTITY() AS bigint);
END

-- Normalise to a STANDALONE top-level entry (self-heals an older row that was nested under Customers).
UPDATE app.ModuleMaster
SET ModuleDisplayName = 'Module Group Authority', ModuleHeadName = 'Module Group Authority',
    ModuleHeadDisplayName = 'Module Group Authority', ModuleDisplayOrder = 1.0, SetGroupIndex = 2.55,
    ModuleIcon = 'Layers', ParentModuleName = NULL, IsSidebarItem = 1
WHERE ModuleID = @nid;

-- Clone grants from the template module to the new module id (dynamic column set, minus identity).
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

SELECT ModuleID, ModuleName, ModuleDisplayName, ModuleIcon, IsSidebarItem,
       (SELECT COUNT(*) FROM app.UserModuleAuthentication WHERE ModuleID=@nid) AS Grants
FROM app.ModuleMaster WHERE ModuleID = @nid;
