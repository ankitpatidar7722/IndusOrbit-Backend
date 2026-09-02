-- Sidebar group "Implementation Process" with 5 sub-modules (the client-detail tabs surfaced
-- as their own routes: Kick-Off, Tracker, Template Master Excel, Sign-Off, Onsite Management).
-- Idempotent: inserts a ModuleMaster row per route only if missing (ModuleID is IDENTITY — never
-- insert it), and grants CanView to every user who already sees /clients (same audience), copying
-- that user's /clients permission levels. Placed between Clients (2.0) and Customers (2.5).
SET NOCOUNT ON;

DECLARE @grp float = 2.2;               -- SetGroupIndex → order among sidebar groups
DECLARE @head nvarchar(200) = 'Implementation';

-- (route, label, icon, order) for the 5 sub-modules.
DECLARE @mods TABLE (Route nvarchar(200), Label nvarchar(200), Icon varchar(50), Ord real);
INSERT INTO @mods VALUES
    ('/implementation/kickoff',   'Kick-Off',              'Rocket',          1),
    ('/implementation/tracker',   'Tracker',               'Activity',        2),
    ('/implementation/templates', 'Template Master Excel', 'FileSpreadsheet', 3),
    ('/implementation/signoff',   'Sign-Off',              'FileCheck2',      4),
    ('/implementation/onsite',    'Onsite Management',     'HardHat',         5);

-- 1) Insert the ModuleMaster rows (skip any that already exist).
INSERT INTO app.ModuleMaster
    (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName, ModuleDisplayOrder,
     SetGroupIndex, ModuleIcon, ParentModuleName, ReactRoutePath, CompanyID, IsDeletedTransaction, IsSidebarItem)
SELECT m.Route, m.Label, @head, @head, m.Ord, @grp, m.Icon, NULL, m.Route, 1, 0, 1
FROM @mods m
WHERE NOT EXISTS (
    SELECT 1 FROM app.ModuleMaster e WHERE e.ModuleName = m.Route AND e.CompanyID = 1
);

-- 2) Grant CanView (mirroring each user's /clients permission levels) to everyone who can see /clients.
DECLARE @clientsModuleId bigint = (SELECT TOP 1 ModuleID FROM app.ModuleMaster WHERE ModuleName = '/clients' AND CompanyID = 1);

INSERT INTO app.UserModuleAuthentication
    (UserID, ModuleID, CanView, CanSave, CanEdit, CanDelete, CanPrint, CanExport, CanCancel, CompanyID, IsDeletedTransaction)
SELECT src.UserID, nm.ModuleID, src.CanView, src.CanSave, src.CanEdit, src.CanDelete, src.CanPrint, src.CanExport, src.CanCancel, 1, 0
FROM app.UserModuleAuthentication src
JOIN app.ModuleMaster nm ON nm.ModuleHeadName = @head AND nm.CompanyID = 1 AND ISNULL(nm.IsDeletedTransaction,0) = 0
WHERE src.ModuleID = @clientsModuleId AND src.CanView = 1 AND ISNULL(src.IsDeletedTransaction,0) = 0
  AND NOT EXISTS (
    SELECT 1 FROM app.UserModuleAuthentication x WHERE x.UserID = src.UserID AND x.ModuleID = nm.ModuleID
  );

-- Report what now exists.
SELECT ModuleID, ModuleName, ModuleDisplayName, SetGroupIndex, ModuleDisplayOrder
FROM app.ModuleMaster WHERE ModuleHeadName = @head ORDER BY ModuleDisplayOrder;
