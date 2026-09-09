-- Web Push subscriptions (one row per browser/device endpoint per user).
-- NOTE: the app also creates this table lazily on first use (PushSubscriptionRepository.EnsureAsync),
-- so a publish-only deploy needs NO manual run. This script is the reference migration and can be
-- run ahead of deploy to pre-provision the table. Idempotent (IF NOT EXISTS guards). Runs on the
-- app/Default DB (IndusOrbit on server, IndusTaskManagement locally) — the `app` schema.
--
-- EndpointHash is a PLAIN VARBINARY(32) that the app fills with SHA-256(endpoint) in C# (NOT a
-- computed column) — so the unique index needs no special ANSI SET options and this runs on any
-- client, including plain sqlcmd.

IF SCHEMA_ID('app') IS NULL EXEC('CREATE SCHEMA app');

IF OBJECT_ID('app.PushSubscriptions','U') IS NULL
BEGIN
    CREATE TABLE app.PushSubscriptions (
        Id           BIGINT IDENTITY(1,1) CONSTRAINT PK_PushSubscriptions PRIMARY KEY,
        UserID       BIGINT NOT NULL,
        CompanyID    INT NOT NULL CONSTRAINT DF_PushSubscriptions_Company DEFAULT 1,
        Endpoint     NVARCHAR(2048) NOT NULL,
        EndpointHash VARBINARY(32) NOT NULL,
        P256dh       NVARCHAR(300) NOT NULL,
        Auth         NVARCHAR(200) NOT NULL,
        UserAgent    NVARCHAR(400) NULL,
        CreatedAt    DATETIME2 NOT NULL CONSTRAINT DF_PushSubscriptions_Created DEFAULT SYSUTCDATETIME(),
        LastSeenAt   DATETIME2 NOT NULL CONSTRAINT DF_PushSubscriptions_Seen DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_PushSubscriptions_EndpointHash ON app.PushSubscriptions(EndpointHash);
    CREATE INDEX IX_PushSubscriptions_UserID ON app.PushSubscriptions(UserID);
    PRINT 'Created app.PushSubscriptions';
END
ELSE
    PRINT 'app.PushSubscriptions already exists — no change';
