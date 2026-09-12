-- Add "SOP of Web Modules" to the existing "Implementation" sidebar group (SetGroupIndex 2.2),
-- as the 6th item after Onsite Management. Idempotent: inserts the ModuleMaster row only if missing
-- (ModuleID is IDENTITY — never insert it), and grants CanView to every user who already sees
-- /clients (same audience), copying that user's /clients permission levels.
SET NOCOUNT ON;

DECLARE @grp float = 2.2;                         -- same group as the other Implementation items
DECLARE @head nvarchar(200) = 'Implementation';
DECLARE @route nvarchar(200) = '/implementation/sop-web-modules';

-- 1) Insert the ModuleMaster row (skip if it already exists).
INSERT INTO app.ModuleMaster
    (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName, ModuleDisplayOrder,
     SetGroupIndex, ModuleIcon, ParentModuleName, ReactRoutePath, CompanyID, IsDeletedTransaction, IsSidebarItem)
SELECT @route, 'SOP of Web Modules', @head, @head, 6, @grp, 'BookOpen', NULL, @route, 1, 0, 1
WHERE NOT EXISTS (
    SELECT 1 FROM app.ModuleMaster e WHERE e.ModuleName = @route AND e.CompanyID = 1
);

-- 2) Grant CanView (mirroring each user's /clients permission levels) to everyone who can see /clients.
DECLARE @clientsModuleId bigint = (SELECT TOP 1 ModuleID FROM app.ModuleMaster WHERE ModuleName = '/clients' AND CompanyID = 1);
DECLARE @newModuleId   bigint = (SELECT TOP 1 ModuleID FROM app.ModuleMaster WHERE ModuleName = @route AND CompanyID = 1);

INSERT INTO app.UserModuleAuthentication
    (UserID, ModuleID, CanView, CanSave, CanEdit, CanDelete, CanPrint, CanExport, CanCancel, CompanyID, IsDeletedTransaction)
SELECT src.UserID, @newModuleId, src.CanView, src.CanSave, src.CanEdit, src.CanDelete, src.CanPrint, src.CanExport, src.CanCancel, 1, 0
FROM app.UserModuleAuthentication src
WHERE src.ModuleID = @clientsModuleId AND src.CanView = 1 AND ISNULL(src.IsDeletedTransaction,0) = 0
  AND NOT EXISTS (
    SELECT 1 FROM app.UserModuleAuthentication x WHERE x.UserID = src.UserID AND x.ModuleID = @newModuleId
  );

-- Report.
SELECT ModuleID, ModuleName, ModuleDisplayName, SetGroupIndex, ModuleDisplayOrder
FROM app.ModuleMaster WHERE ModuleName = @route AND CompanyID = 1;
