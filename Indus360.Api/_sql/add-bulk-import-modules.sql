-- "Bulk Import" sidebar group + its submodules (Indus360-hosted BulkImport modules).
-- Idempotent — safe to re-run on any app DB. Add one INSERT block per submodule as they are built.

-- Place the "Bulk Import" group just BEFORE the "Activity" group. Reuse the group's existing index if
-- already present; otherwise slot it 0.5 under Activity's SetGroupIndex (falls back to 3 if Activity absent).
DECLARE @grpSgi FLOAT = (SELECT MIN(SetGroupIndex) FROM app.ModuleMaster WHERE ModuleHeadName = 'Bulk Import');
IF @grpSgi IS NULL SET @grpSgi = ISNULL((SELECT MIN(SetGroupIndex) FROM app.ModuleMaster WHERE ModuleHeadName = 'Activity'), 3.5) - 0.5;

-- Import Master
IF NOT EXISTS (SELECT 1 FROM app.ModuleMaster WHERE ModuleName = '/bulk-import/import-master')
    INSERT INTO app.ModuleMaster
        (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
         ModuleDisplayOrder, SetGroupIndex, ModuleIcon, ParentModuleName, ReactRoutePath,
         CompanyID, IsDeletedTransaction, IsSidebarItem)
    VALUES
        ('/bulk-import/import-master', 'Import Master', 'Bulk Import', 'Bulk Import',
         1, @grpSgi, 'FileSpreadsheet', NULL, NULL, 1, 0, 1);

-- Stock Upload
IF NOT EXISTS (SELECT 1 FROM app.ModuleMaster WHERE ModuleName = '/bulk-import/stock-upload')
    INSERT INTO app.ModuleMaster
        (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
         ModuleDisplayOrder, SetGroupIndex, ModuleIcon, ParentModuleName, ReactRoutePath,
         CompanyID, IsDeletedTransaction, IsSidebarItem)
    VALUES
        ('/bulk-import/stock-upload', 'Stock Upload', 'Bulk Import', 'Bulk Import',
         2, @grpSgi, 'PackagePlus', NULL, NULL, 1, 0, 1);

-- Module Authority
IF NOT EXISTS (SELECT 1 FROM app.ModuleMaster WHERE ModuleName = '/bulk-import/module-authority')
    INSERT INTO app.ModuleMaster
        (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
         ModuleDisplayOrder, SetGroupIndex, ModuleIcon, ParentModuleName, ReactRoutePath,
         CompanyID, IsDeletedTransaction, IsSidebarItem)
    VALUES
        ('/bulk-import/module-authority', 'Module Authority', 'Bulk Import', 'Bulk Import',
         3, @grpSgi, 'ShieldCheck', NULL, NULL, 1, 0, 1);

-- Manage Plans (global plan catalog)
IF NOT EXISTS (SELECT 1 FROM app.ModuleMaster WHERE ModuleName = '/bulk-import/manage-plans')
    INSERT INTO app.ModuleMaster
        (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
         ModuleDisplayOrder, SetGroupIndex, ModuleIcon, ParentModuleName, ReactRoutePath,
         CompanyID, IsDeletedTransaction, IsSidebarItem)
    VALUES
        ('/bulk-import/manage-plans', 'Manage Plans', 'Bulk Import', 'Bulk Import',
         4, @grpSgi, 'Tags', NULL, NULL, 1, 0, 1);

-- Feature Subscription
IF NOT EXISTS (SELECT 1 FROM app.ModuleMaster WHERE ModuleName = '/bulk-import/feature-subscription')
    INSERT INTO app.ModuleMaster
        (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
         ModuleDisplayOrder, SetGroupIndex, ModuleIcon, ParentModuleName, ReactRoutePath,
         CompanyID, IsDeletedTransaction, IsSidebarItem)
    VALUES
        ('/bulk-import/feature-subscription', 'Feature Subscription', 'Bulk Import', 'Bulk Import',
         5, @grpSgi, 'BadgeCheck', NULL, NULL, 1, 0, 1);

-- Content Authority
IF NOT EXISTS (SELECT 1 FROM app.ModuleMaster WHERE ModuleName = '/bulk-import/content-authority')
    INSERT INTO app.ModuleMaster
        (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
         ModuleDisplayOrder, SetGroupIndex, ModuleIcon, ParentModuleName, ReactRoutePath,
         CompanyID, IsDeletedTransaction, IsSidebarItem)
    VALUES
        ('/bulk-import/content-authority', 'Content Authority', 'Bulk Import', 'Bulk Import',
         6, @grpSgi, 'Images', NULL, NULL, 1, 0, 1);

-- KeyLine Generator (dieline/keyline CAD + 3D box-folding preview)
IF NOT EXISTS (SELECT 1 FROM app.ModuleMaster WHERE ModuleName = '/bulk-import/keyline-generator')
    INSERT INTO app.ModuleMaster
        (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
         ModuleDisplayOrder, SetGroupIndex, ModuleIcon, ParentModuleName, ReactRoutePath,
         CompanyID, IsDeletedTransaction, IsSidebarItem)
    VALUES
        ('/bulk-import/keyline-generator', 'KeyLine Generator', 'Bulk Import', 'Bulk Import',
         7, @grpSgi, 'PenTool', NULL, NULL, 1, 0, 1);

-- ERP Transaction Delete (destructive master/transaction cleanup)
IF NOT EXISTS (SELECT 1 FROM app.ModuleMaster WHERE ModuleName = '/bulk-import/erp-transaction-delete')
    INSERT INTO app.ModuleMaster
        (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
         ModuleDisplayOrder, SetGroupIndex, ModuleIcon, ParentModuleName, ReactRoutePath,
         CompanyID, IsDeletedTransaction, IsSidebarItem)
    VALUES
        ('/bulk-import/erp-transaction-delete', 'ERP Transaction Delete', 'Bulk Import', 'Bulk Import',
         8, @grpSgi, 'Trash2', NULL, NULL, 1, 0, 1);

-- Company Master (full 13-tab company profile editor — same fields as BulkImport, Indus360 UI)
IF NOT EXISTS (SELECT 1 FROM app.ModuleMaster WHERE ModuleName = '/bulk-import/company-master')
    INSERT INTO app.ModuleMaster
        (ModuleName, ModuleDisplayName, ModuleHeadName, ModuleHeadDisplayName,
         ModuleDisplayOrder, SetGroupIndex, ModuleIcon, ParentModuleName, ReactRoutePath,
         CompanyID, IsDeletedTransaction, IsSidebarItem)
    VALUES
        ('/bulk-import/company-master', 'Company Master', 'Bulk Import', 'Bulk Import',
         9, @grpSgi, 'Building2', NULL, NULL, 1, 0, 1);

-- Grant every Admin-group viewer the same rights on each new Bulk Import module (mirror /audit-logs).
DECLARE @auditId INT = (SELECT ModuleID FROM app.ModuleMaster WHERE ModuleName = '/audit-logs');
INSERT INTO app.UserModuleAuthentication
    (UserID, ModuleID, CanView, CanSave, CanEdit, CanDelete, CanPrint, CanExport, CanCancel, CompanyID, IsDeletedTransaction)
SELECT a.UserID, m.ModuleID, a.CanView, a.CanSave, a.CanEdit, a.CanDelete, a.CanPrint, a.CanExport, a.CanCancel, a.CompanyID, 0
FROM app.ModuleMaster m
CROSS JOIN app.UserModuleAuthentication a
WHERE m.ModuleHeadName = 'Bulk Import'
  AND a.ModuleID = @auditId AND a.CanView = 1
  AND NOT EXISTS (SELECT 1 FROM app.UserModuleAuthentication x WHERE x.UserID = a.UserID AND x.ModuleID = m.ModuleID);
