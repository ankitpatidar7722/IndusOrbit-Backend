-- Audit log for Module Group deletions (who deleted, which group, reason, when).
-- Local app DB (IndusTaskManagement). Idempotent.
SET NOCOUNT ON;
IF OBJECT_ID('app.ModuleGroupDeletionLog', 'U') IS NULL
BEGIN
    CREATE TABLE app.ModuleGroupDeletionLog (
        Id               int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ApplicationName  nvarchar(100)  NULL,
        ModuleGroupName  nvarchar(200)  NULL,
        ModulesDeleted   int            NOT NULL DEFAULT 0,
        Reason           nvarchar(max)  NULL,
        DeletedByUserId  bigint         NULL,
        DeletedByName    nvarchar(200)  NULL,
        DeletedByEmail   nvarchar(256)  NULL,
        DeletedAt        datetime2(0)   NOT NULL DEFAULT SYSDATETIME()
    );
    SELECT 'app.ModuleGroupDeletionLog created' AS Result;
END
ELSE SELECT 'app.ModuleGroupDeletionLog already exists' AS Result;
