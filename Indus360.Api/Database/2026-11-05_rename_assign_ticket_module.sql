-- 2026-11-05 — Rename the Point Management sidebar item "Assign / Create Ticket" to "Assign Ticket".
-- Display name only; the route and permissions stay the same. Safe to re-run.
-- Run on the Indus360 app DB (local IndusTaskManagement / prod IndusOrbit).

UPDATE app.ModuleMaster
SET ModuleDisplayName = 'Assign Ticket'
WHERE ModuleName = '/point-management/assign'
  AND ModuleDisplayName <> 'Assign Ticket';

SELECT ModuleID, ModuleName, ModuleDisplayName FROM app.ModuleMaster WHERE ModuleName = '/point-management/assign';
