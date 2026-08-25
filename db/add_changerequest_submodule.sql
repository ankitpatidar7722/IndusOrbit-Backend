-- Sub Module Name for client Change Requests (keyline ModuleDisplayName). Idempotent.
SET NOCOUNT ON;
IF COL_LENGTH('app.ChangeRequests', 'SubModule') IS NULL
BEGIN
    ALTER TABLE app.ChangeRequests ADD SubModule nvarchar(200) NULL;
    SELECT 'SubModule column added' AS Result;
END
ELSE SELECT 'SubModule already exists' AS Result;
