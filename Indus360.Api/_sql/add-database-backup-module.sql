-- Adds the Admin -> "Database Backup" sidebar module (/admin/database-backup) and grants view to
-- everyone who can already see the Admin group. Idempotent — safe to run on any app DB (Local + IndusOrbit).
-- Run once per app database at deploy time.

IF NOT EXISTS (SELECT 1 FROM app.ModuleMaster WHERE ModuleName = '/admin/database-backup')
BEGIN
    INSERT INTO app.ModuleMaster
        (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
         ModuleDisplayOrder, SetGroupIndex, ModuleIcon, ParentModuleName, ReactRoutePath,
         CompanyID, IsDeletedTransaction, IsSidebarItem)
    VALUES
        ('/admin/database-backup', 'Database Backup', 'Admin', 'Admin',
         5, 4, 'Database', NULL, NULL,
         1, 0, 1);
END

DECLARE @newId   INT = (SELECT ModuleID FROM app.ModuleMaster WHERE ModuleName = '/admin/database-backup');
DECLARE @auditId INT = (SELECT ModuleID FROM app.ModuleMaster WHERE ModuleName = '/audit-logs');

-- Mirror each Admin-group viewer's /audit-logs permissions onto the new module (only if missing).
INSERT INTO app.UserModuleAuthentication
    (UserID, ModuleID, CanView, CanSave, CanEdit, CanDelete, CanPrint, CanExport, CanCancel, CompanyID, IsDeletedTransaction)
SELECT a.UserID, @newId, a.CanView, a.CanSave, a.CanEdit, a.CanDelete, a.CanPrint, a.CanExport, a.CanCancel, a.CompanyID, 0
FROM app.UserModuleAuthentication a
WHERE a.ModuleID = @auditId
  AND a.CanView = 1
  AND NOT EXISTS (SELECT 1 FROM app.UserModuleAuthentication x WHERE x.UserID = a.UserID AND x.ModuleID = @newId);
