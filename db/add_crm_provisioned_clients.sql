-- Tracks which CRM (IndusInternalApp) clients have already had a database provisioned
-- through the /clients "Create Client Project" wizard's CRM Client picker, so the picker
-- can show a "DB Status = Created" column and the admin doesn't accidentally re-provision.
-- Lives in Indus360's OWN database (app schema) — deliberately NOT written onto the CRM
-- app's IndusAppDB, to keep ownership boundaries clean (Indus360 only ever READS from CRM).
SET NOCOUNT ON;
IF OBJECT_ID('app.CrmProvisionedClients', 'U') IS NULL
BEGIN
    CREATE TABLE app.CrmProvisionedClients (
        Id            INT IDENTITY(1,1) PRIMARY KEY,
        CrmCustomerID INT NOT NULL,
        ClientName    NVARCHAR(200) NULL,
        DatabaseName  NVARCHAR(200) NULL,
        CreatedAt     DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        CreatedBy     INT NULL
    );
    CREATE UNIQUE INDEX IX_CrmProvisionedClients_CrmCustomerID ON app.CrmProvisionedClients(CrmCustomerID);
END
