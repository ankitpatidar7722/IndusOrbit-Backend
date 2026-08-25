-- Module + Sub Module (keyline) for Point Management points. Idempotent.
SET NOCOUNT ON;
IF COL_LENGTH('dbo.Points', 'Module') IS NULL
    ALTER TABLE dbo.Points ADD Module nvarchar(200) NULL;
IF COL_LENGTH('dbo.Points', 'SubModule') IS NULL
    ALTER TABLE dbo.Points ADD SubModule nvarchar(200) NULL;
SELECT 'Points.Module/SubModule ready' AS Result;
