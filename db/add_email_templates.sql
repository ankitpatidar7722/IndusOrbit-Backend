-- Reusable email templates managed from /email → Templates. Subject/Body carry
-- {{placeholder}} tokens; the frontend derives the variable list from them.
-- Idempotent: safe to re-run (creates the table, or adds any missing column).
SET NOCOUNT ON;

IF OBJECT_ID('app.EmailTemplates') IS NULL
BEGIN
    CREATE TABLE app.EmailTemplates (
        Id                   int IDENTITY(1,1) PRIMARY KEY,
        Name                 nvarchar(200)  NOT NULL,
        Subject              nvarchar(400)  NOT NULL CONSTRAINT DF_EmailTemplates_Subject DEFAULT(''),
        Body                 nvarchar(max)  NOT NULL CONSTRAINT DF_EmailTemplates_Body    DEFAULT(''),
        Category             nvarchar(80)   NULL,
        IsActive             bit            NOT NULL CONSTRAINT DF_EmailTemplates_IsActive DEFAULT(1),
        IsDeletedTransaction bit            NOT NULL CONSTRAINT DF_EmailTemplates_Del      DEFAULT(0),
        CreatedAt            datetime2      NOT NULL CONSTRAINT DF_EmailTemplates_Created  DEFAULT(SYSUTCDATETIME()),
        UpdatedAt            datetime2      NOT NULL CONSTRAINT DF_EmailTemplates_Updated  DEFAULT(SYSUTCDATETIME())
    );
    SELECT 'app.EmailTemplates created' AS Result;
END
ELSE IF COL_LENGTH('app.EmailTemplates', 'IsDeletedTransaction') IS NULL
BEGIN
    ALTER TABLE app.EmailTemplates ADD IsDeletedTransaction bit NOT NULL CONSTRAINT DF_EmailTemplates_Del DEFAULT(0);
    SELECT 'IsDeletedTransaction column added' AS Result;
END
ELSE SELECT 'app.EmailTemplates already up to date' AS Result;
