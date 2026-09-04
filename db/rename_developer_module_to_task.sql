-- Rename the Point Management "Developer" sidebar module label from "Developer Dashboard"
-- to "Developer Task" (developer-facing name for the page where they work their assigned points).
-- Idempotent: only the display label changes; the route (ModuleName) stays '/point-management/developer'.
UPDATE app.ModuleMaster
SET ModuleDisplayName = 'Developer Task'
WHERE ModuleName = '/point-management/developer';
