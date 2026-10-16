/* ============================================================================
   Indus 360 — DB objects for the 08-Oct-2026 deploy
   Run on the PRODUCTION app database (Server mode = IndusOrbit, `app` schema).

   Safe + idempotent: every object is guarded, so running it more than once (or
   after the app has already lazy-created a table) does nothing harmful.

   NOTE: nothing here is strictly required — the backend lazy-creates both tables
   on their first write. This script just lets you pre-create them so they exist
   and are verified up front.

   Password hashing needs NO script: app.Users.PasswordHash already exists; each
   user's plain password is re-hashed to BCrypt automatically on their next login.
   ============================================================================ */

/* Make sure the `app` schema exists (it already does in IndusOrbit). */
IF SCHEMA_ID('app') IS NULL EXEC('CREATE SCHEMA app');
GO

/* ---------- 1) app.AuditLog — the activity / audit trail ---------- */
IF OBJECT_ID('app.AuditLog','U') IS NULL
BEGIN
    CREATE TABLE app.AuditLog (
        Id          BIGINT IDENTITY(1,1) CONSTRAINT PK_AuditLog PRIMARY KEY,
        CreatedAt   DATETIME2 NOT NULL CONSTRAINT DF_AuditLog_CreatedAt DEFAULT SYSUTCDATETIME(),
        UserId      INT NULL,
        UserName    NVARCHAR(200) NULL,
        [Action]    NVARCHAR(40) NOT NULL,
        Module      NVARCHAR(120) NULL,
        SubModule   NVARCHAR(120) NULL,
        Client      NVARCHAR(150) NULL,
        EntityType  NVARCHAR(120) NULL,
        EntityId    NVARCHAR(100) NULL,
        Summary     NVARCHAR(500) NULL,
        Changes     NVARCHAR(MAX) NULL,
        HttpMethod  NVARCHAR(10) NULL,
        [Path]      NVARCHAR(500) NULL,
        StatusCode  INT NULL,
        Success     BIT NULL,
        DurationMs  INT NULL,
        IpAddress   NVARCHAR(60) NULL,
        Browser     NVARCHAR(80) NULL,
        Os          NVARCHAR(80) NULL,
        Device      NVARCHAR(40) NULL,
        DeviceId    NVARCHAR(80) NULL,
        Fingerprint NVARCHAR(40) NULL,
        UserAgent   NVARCHAR(500) NULL,
        Payload     NVARCHAR(MAX) NULL
    );
    CREATE INDEX IX_AuditLog_CreatedAt ON app.AuditLog(CreatedAt DESC);
    CREATE INDEX IX_AuditLog_User      ON app.AuditLog(UserId);
    CREATE INDEX IX_AuditLog_Module    ON app.AuditLog(Module);
    CREATE INDEX IX_AuditLog_Action    ON app.AuditLog([Action]);
END
ELSE
BEGIN
    /* table already present (e.g. app lazy-created it) → add any newer columns */
    IF COL_LENGTH('app.AuditLog','SubModule')   IS NULL ALTER TABLE app.AuditLog ADD SubModule   NVARCHAR(120) NULL;
    IF COL_LENGTH('app.AuditLog','Client')      IS NULL ALTER TABLE app.AuditLog ADD Client      NVARCHAR(150) NULL;
    IF COL_LENGTH('app.AuditLog','DeviceId')    IS NULL ALTER TABLE app.AuditLog ADD DeviceId    NVARCHAR(80) NULL;
    IF COL_LENGTH('app.AuditLog','Fingerprint') IS NULL ALTER TABLE app.AuditLog ADD Fingerprint NVARCHAR(40) NULL;
END
GO

/* ---------- 2) app.LoginSession — login / session duration tracking ---------- */
IF OBJECT_ID('app.LoginSession','U') IS NULL
BEGIN
    CREATE TABLE app.LoginSession (
        Id          BIGINT IDENTITY(1,1) CONSTRAINT PK_LoginSession PRIMARY KEY,
        UserId      INT NULL,
        UserName    NVARCHAR(200) NULL,
        LoginAt     DATETIME2 NOT NULL CONSTRAINT DF_LoginSession_LoginAt  DEFAULT SYSUTCDATETIME(),
        LastSeen    DATETIME2 NOT NULL CONSTRAINT DF_LoginSession_LastSeen DEFAULT SYSUTCDATETIME(),
        LogoutAt    DATETIME2 NULL,
        DurationMin INT NULL,
        EndReason   NVARCHAR(40) NULL,
        Active      BIT NOT NULL CONSTRAINT DF_LoginSession_Active DEFAULT 1,
        IpAddress   NVARCHAR(60) NULL,
        Browser     NVARCHAR(80) NULL,
        Os          NVARCHAR(80) NULL,
        Device      NVARCHAR(40) NULL,
        DeviceId    NVARCHAR(80) NULL
    );
    CREATE INDEX IX_LoginSession_User   ON app.LoginSession(UserId);
    CREATE INDEX IX_LoginSession_Active ON app.LoginSession(Active);
    CREATE INDEX IX_LoginSession_Login  ON app.LoginSession(LoginAt DESC);
END
ELSE IF COL_LENGTH('app.LoginSession','DeviceId') IS NULL
    ALTER TABLE app.LoginSession ADD DeviceId NVARCHAR(80) NULL;
GO

PRINT 'Done: app.AuditLog + app.LoginSession are ready.';
