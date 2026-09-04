-- =====================================================================================
-- Indus 360 — DEPLOY-READINESS AUDIT (READ-ONLY, safe to run anywhere)
-- Run this on the PRODUCTION IndusOrbit DB (52.66.183.53). Any row showing 'MISSING'
-- means the named migration in Backend/db has NOT been applied on the server yet —
-- run that one script (all migrations are idempotent / IF-NOT-EXISTS guarded).
-- =====================================================================================
SET NOCOUNT ON;

SELECT Object_, Migration_, Status_ FROM (
  SELECT 1 AS ord, 'app.ChangeRequests.PointID (col)' AS Object_, 'add_changerequest_pointid.sql' AS Migration_,
         CASE WHEN COL_LENGTH('app.ChangeRequests','PointID') IS NOT NULL THEN 'EXISTS' ELSE 'MISSING' END AS Status_
  UNION ALL SELECT 2, 'app.ChangeRequests.SubModule (col)', 'add_changerequest_submodule.sql',
         CASE WHEN COL_LENGTH('app.ChangeRequests','SubModule') IS NOT NULL THEN 'EXISTS' ELSE 'MISSING' END
  UNION ALL SELECT 3, 'app.Users.EmailSignature (col)', 'add_user_email_signature.sql',
         CASE WHEN COL_LENGTH('app.Users','EmailSignature') IS NOT NULL THEN 'EXISTS' ELSE 'MISSING' END
  UNION ALL SELECT 4, 'dbo.Points.Module (col)', 'add_point_module_submodule.sql',
         CASE WHEN COL_LENGTH('dbo.Points','Module') IS NOT NULL THEN 'EXISTS' ELSE 'MISSING' END
  UNION ALL SELECT 5, 'dbo.Points.SubModule (col)', 'add_point_module_submodule.sql',
         CASE WHEN COL_LENGTH('dbo.Points','SubModule') IS NOT NULL THEN 'EXISTS' ELSE 'MISSING' END
  UNION ALL SELECT 6, 'app.CrmProvisionedClients (table)', 'add_crm_provisioned_clients.sql',
         CASE WHEN OBJECT_ID('app.CrmProvisionedClients','U') IS NOT NULL THEN 'EXISTS' ELSE 'MISSING' END
  UNION ALL SELECT 7, 'app.EmailTemplates (table)', 'add_email_templates.sql',
         CASE WHEN OBJECT_ID('app.EmailTemplates','U') IS NOT NULL THEN 'EXISTS' ELSE 'MISSING' END
  UNION ALL SELECT 8, 'app.EmailTemplates.IsDeletedTransaction (col)', 'add_email_templates.sql',
         CASE WHEN OBJECT_ID('app.EmailTemplates','U') IS NULL THEN 'n/a (table missing)'
              WHEN COL_LENGTH('app.EmailTemplates','IsDeletedTransaction') IS NOT NULL THEN 'EXISTS' ELSE 'MISSING' END
  UNION ALL SELECT 9, 'app.ModuleGroupDeletionLog (table)', 'add_module_group_deletion_log.sql',
         CASE WHEN OBJECT_ID('app.ModuleGroupDeletionLog','U') IS NOT NULL THEN 'EXISTS' ELSE 'MISSING' END
  UNION ALL SELECT 10, 'app.UserProjectAssignment (table)', 'project-assignment (consumer scoping dep)',
         CASE WHEN OBJECT_ID('app.UserProjectAssignment','U') IS NOT NULL THEN 'EXISTS' ELSE 'MISSING' END
  UNION ALL SELECT 11, 'app.Milestones (table)', 'tracker milestone template',
         CASE WHEN OBJECT_ID('app.Milestones','U') IS NOT NULL THEN 'EXISTS' ELSE 'MISSING' END
  UNION ALL SELECT 12, 'Sidebar module: /email', 'add_email_module.sql',
         CASE WHEN EXISTS(SELECT 1 FROM app.ModuleMaster WHERE ModuleName='/email' AND ISNULL(IsDeletedTransaction,0)=0) THEN 'EXISTS' ELSE 'MISSING' END
  UNION ALL SELECT 13, 'Sidebar module: /module-group-authority', 'add_module_group_authority_module.sql',
         CASE WHEN EXISTS(SELECT 1 FROM app.ModuleMaster WHERE ModuleName='/module-group-authority' AND ISNULL(IsDeletedTransaction,0)=0) THEN 'EXISTS' ELSE 'MISSING' END
  UNION ALL SELECT 14, 'Sidebar modules: /implementation/* (expect 5)', 'add_implementation_process_sidebar.sql',
         CAST((SELECT COUNT(*) FROM app.ModuleMaster WHERE ModuleName LIKE '/implementation/%' AND ISNULL(IsDeletedTransaction,0)=0) AS varchar(10)) + ' found'
  UNION ALL SELECT 15, 'dbo.Points.IsDeletedTransaction (col)', 'add_point_isdeleted.sql',
         CASE WHEN COL_LENGTH('dbo.Points','IsDeletedTransaction') IS NOT NULL THEN 'EXISTS' ELSE 'MISSING' END
  UNION ALL SELECT 16, 'Sidebar label: /point-management/developer = ''Developer Task''', 'rename_developer_module_to_task.sql',
         CASE WHEN EXISTS(SELECT 1 FROM app.ModuleMaster WHERE ModuleName='/point-management/developer' AND ModuleDisplayName='Developer Task') THEN 'EXISTS' ELSE 'MISSING (still ''Developer Dashboard'')' END
) t
ORDER BY ord;
