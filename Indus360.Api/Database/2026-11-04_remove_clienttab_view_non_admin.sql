-- 2026-11-04 — Remove "Client Detail Tabs" VIEW authority from every user whose role is not admin.
-- Only CanView is changed, only for the modules under ModuleHeadName = 'Client Detail Tabs'
-- (clienttab-company / -authority / -kickoff / -tracker / -templates / -signoff / -onsite).
-- Admin users keep their rights. Run on the Indus360 app DB (local IndusTaskManagement / prod IndusOrbit).

-- 1) Preview: what will change
SELECT u.UserId, u.FullName, u.Role, m.ModuleDisplayName
FROM app.UserModuleAuthentication a
JOIN app.ModuleMaster m ON m.ModuleID = a.ModuleID AND m.ModuleHeadName = 'Client Detail Tabs'
JOIN app.Users u        ON u.UserId = a.UserID
WHERE a.CanView = 1 AND ISNULL(a.IsDeletedTransaction, 0) = 0
  AND LOWER(LTRIM(RTRIM(ISNULL(u.Role, '')))) <> 'admin'
ORDER BY u.FullName, m.ModuleID;

-- 2) Apply
BEGIN TRAN;
UPDATE a SET a.CanView = 0
FROM app.UserModuleAuthentication a
JOIN app.ModuleMaster m ON m.ModuleID = a.ModuleID AND m.ModuleHeadName = 'Client Detail Tabs'
WHERE a.CanView = 1 AND ISNULL(a.IsDeletedTransaction, 0) = 0
  AND NOT EXISTS (SELECT 1 FROM app.Users u WHERE u.UserId = a.UserID AND LOWER(LTRIM(RTRIM(u.Role))) = 'admin');
SELECT @@ROWCOUNT AS RowsUpdated;

-- A user with NO clienttab rows at all is treated as "view every tab" (ClientTabPermissionRepository default),
-- so give every non-admin an explicit all-zero row for each tab they don't have a row for yet.
INSERT INTO app.UserModuleAuthentication (UserID, ModuleID, CanView, CanSave, CanEdit, CanDelete, CanPrint, CanExport, CanCancel, CompanyID, IsDeletedTransaction)
SELECT u.UserId, m.ModuleID, 0,0,0,0,0,0,0, ISNULL(u.CompanyId, 1), 0
FROM app.Users u
CROSS JOIN app.ModuleMaster m
WHERE m.ModuleHeadName = 'Client Detail Tabs' AND ISNULL(m.IsDeletedTransaction,0) = 0
  AND ISNULL(u.IsDeletedTransaction,0) = 0
  AND LOWER(LTRIM(RTRIM(ISNULL(u.Role,'')))) <> 'admin'
  AND NOT EXISTS (SELECT 1 FROM app.UserModuleAuthentication a WHERE a.UserID = u.UserId AND a.ModuleID = m.ModuleID AND ISNULL(a.IsDeletedTransaction,0) = 0);
SELECT @@ROWCOUNT AS DenyRowsInserted;
COMMIT;

-- 3) Check: must return 0 (non-admin users who can still see any client tab)
SELECT COUNT(*) AS NonAdminsStillSeeing
FROM app.Users u
WHERE ISNULL(u.IsDeletedTransaction,0)=0 AND LOWER(LTRIM(RTRIM(ISNULL(u.Role,''))))<>'admin'
  AND (NOT EXISTS (SELECT 1 FROM app.UserModuleAuthentication a JOIN app.ModuleMaster m ON m.ModuleID=a.ModuleID AND m.ModuleHeadName='Client Detail Tabs' WHERE a.UserID=u.UserId AND ISNULL(a.IsDeletedTransaction,0)=0)
    OR EXISTS (SELECT 1 FROM app.UserModuleAuthentication a JOIN app.ModuleMaster m ON m.ModuleID=a.ModuleID AND m.ModuleHeadName='Client Detail Tabs' WHERE a.UserID=u.UserId AND a.CanView=1 AND ISNULL(a.IsDeletedTransaction,0)=0));
