-- Link a Change Request to the Point Management point (ticket) created from it. Idempotent.
SET NOCOUNT ON;
IF COL_LENGTH('app.ChangeRequests', 'PointID') IS NULL
BEGIN
    ALTER TABLE app.ChangeRequests ADD PointID int NULL;
    SELECT 'PointID column added' AS Result;
END
ELSE SELECT 'PointID already exists' AS Result;
