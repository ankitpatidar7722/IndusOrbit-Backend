-- 2026-11-05 — Point Management → "Implementor Dashboard" sidebar page.
-- Adds the module and gives View (+ Print/Export) to active Support and admin users.
-- Safe to re-run. Run on the Indus360 app DB (local IndusTaskManagement / prod IndusOrbit).

SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRAN;

IF NOT EXISTS (SELECT 1 FROM app.ModuleMaster WHERE ModuleName = '/point-management/implementor-dashboard')
    INSERT INTO app.ModuleMaster
        (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName, ModuleDisplayOrder, SetGroupIndex, ModuleIcon, CompanyID, IsDeletedTransaction, IsSidebarItem)
    SELECT '/point-management/implementor-dashboard', 'Implementor Dashboard', ModuleHeadName, ModuleHeadDisplayName, 3.5, SetGroupIndex, 'BarChart3', CompanyID, 0, 1
    FROM app.ModuleMaster
    WHERE ModuleName = '/point-management/developer-kpi';

DECLARE @moduleId INT = (SELECT ModuleID FROM app.ModuleMaster WHERE ModuleName = '/point-management/implementor-dashboard');

INSERT INTO app.UserModuleAuthentication
    (UserID, ModuleID, CanView, CanSave, CanEdit, CanDelete, CanPrint, CanExport, CanCancel, CompanyID, IsDeletedTransaction)
SELECT u.UserId, @moduleId, 1, 0, 0, 0, 1, 1, 0, 1, 0
FROM app.Users u
WHERE u.IsActive = 1 AND ISNULL(u.IsDeletedTransaction, 0) = 0
  AND LOWER(LTRIM(RTRIM(ISNULL(u.Role, '')))) IN ('support', 'admin')
  AND NOT EXISTS (SELECT 1 FROM app.UserModuleAuthentication a WHERE a.UserID = u.UserId AND a.ModuleID = @moduleId);

SELECT @moduleId AS ModuleID, @@ROWCOUNT AS UsersGranted;
COMMIT;
