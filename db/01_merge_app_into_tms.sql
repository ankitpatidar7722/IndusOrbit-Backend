/* ============================================================
   Consolidate the Indus 360 app DB into the Task Management DB.
   Copies Indus360App.dbo.*  ->  IndusTaskManagement.app.*  (new "app" schema)
   so ONE database (IndusTaskManagement) holds everything.

   SAFE / ADDITIVE:
     - Does NOT touch IndusTaskManagement.dbo (the existing TMS tables) — the old
       standalone TMS app keeps working.
     - Does NOT modify Indus360App.
     - Re-runnable (guards on OBJECT_ID / SCHEMA_ID).

   Run this on the server that hosts BOTH databases (same instance).
   After running, point the API's "Default" connection string at
   IndusTaskManagement and the app repositories at the "app" schema.
   ============================================================ */
USE IndusTaskManagement;
GO
IF SCHEMA_ID('app') IS NULL EXEC('CREATE SCHEMA app');
GO
/* 1) structure + data + IDENTITY (SELECT INTO won't overwrite an existing table) */
IF OBJECT_ID('app.ChangeRequests')           IS NULL SELECT * INTO app.ChangeRequests           FROM Indus360App.dbo.ChangeRequests;
IF OBJECT_ID('app.ClientModules')            IS NULL SELECT * INTO app.ClientModules            FROM Indus360App.dbo.ClientModules;
IF OBJECT_ID('app.Clients')                  IS NULL SELECT * INTO app.Clients                  FROM Indus360App.dbo.Clients;
IF OBJECT_ID('app.Employees')                IS NULL SELECT * INTO app.Employees                FROM Indus360App.dbo.Employees;
IF OBJECT_ID('app.Milestones')               IS NULL SELECT * INTO app.Milestones               FROM Indus360App.dbo.Milestones;
IF OBJECT_ID('app.ModuleMaster')             IS NULL SELECT * INTO app.ModuleMaster             FROM Indus360App.dbo.ModuleMaster;
IF OBJECT_ID('app.OnboardingInbox')          IS NULL SELECT * INTO app.OnboardingInbox          FROM Indus360App.dbo.OnboardingInbox;
IF OBJECT_ID('app.OnsiteVisits')             IS NULL SELECT * INTO app.OnsiteVisits             FROM Indus360App.dbo.OnsiteVisits;
IF OBJECT_ID('app.SupportLogs')              IS NULL SELECT * INTO app.SupportLogs              FROM Indus360App.dbo.SupportLogs;
IF OBJECT_ID('app.TrainingUpdates')          IS NULL SELECT * INTO app.TrainingUpdates          FROM Indus360App.dbo.TrainingUpdates;
IF OBJECT_ID('app.UserModuleAuthentication') IS NULL SELECT * INTO app.UserModuleAuthentication FROM Indus360App.dbo.UserModuleAuthentication;
IF OBJECT_ID('app.Users')                    IS NULL SELECT * INTO app.Users                    FROM Indus360App.dbo.Users;
GO
/* 2) primary keys (SELECT INTO keeps IDENTITY but not the PK constraint) */
IF OBJECT_ID('PK_app_ChangeRequests') IS NULL ALTER TABLE app.ChangeRequests           ADD CONSTRAINT PK_app_ChangeRequests PRIMARY KEY (Id);
IF OBJECT_ID('PK_app_ClientModules') IS NULL  ALTER TABLE app.ClientModules            ADD CONSTRAINT PK_app_ClientModules  PRIMARY KEY (Id);
IF OBJECT_ID('PK_app_Clients') IS NULL        ALTER TABLE app.Clients                  ADD CONSTRAINT PK_app_Clients        PRIMARY KEY (ClientCode);
IF OBJECT_ID('PK_app_Employees') IS NULL      ALTER TABLE app.Employees                ADD CONSTRAINT PK_app_Employees      PRIMARY KEY (EmployeeID);
IF OBJECT_ID('PK_app_Milestones') IS NULL     ALTER TABLE app.Milestones               ADD CONSTRAINT PK_app_Milestones     PRIMARY KEY (Id);
IF OBJECT_ID('PK_app_ModuleMaster') IS NULL   ALTER TABLE app.ModuleMaster             ADD CONSTRAINT PK_app_ModuleMaster   PRIMARY KEY (ModuleID);
IF OBJECT_ID('PK_app_OnboardingInbox') IS NULL ALTER TABLE app.OnboardingInbox         ADD CONSTRAINT PK_app_OnboardingInbox PRIMARY KEY (Id);
IF OBJECT_ID('PK_app_OnsiteVisits') IS NULL   ALTER TABLE app.OnsiteVisits             ADD CONSTRAINT PK_app_OnsiteVisits   PRIMARY KEY (Id);
IF OBJECT_ID('PK_app_SupportLogs') IS NULL    ALTER TABLE app.SupportLogs              ADD CONSTRAINT PK_app_SupportLogs    PRIMARY KEY (Id);
IF OBJECT_ID('PK_app_TrainingUpdates') IS NULL ALTER TABLE app.TrainingUpdates         ADD CONSTRAINT PK_app_TrainingUpdates PRIMARY KEY (Id);
IF OBJECT_ID('PK_app_UMA') IS NULL            ALTER TABLE app.UserModuleAuthentication ADD CONSTRAINT PK_app_UMA            PRIMARY KEY (UserModuleAuthenticationID);
IF OBJECT_ID('PK_app_Users') IS NULL          ALTER TABLE app.Users                    ADD CONSTRAINT PK_app_Users          PRIMARY KEY (UserId);
GO
/* 3) Employees default + filtered unique indexes (need QUOTED_IDENTIFIER ON) */
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id=OBJECT_ID('app.Employees') AND name='DF_app_Employees_CanProcureAssets')
    ALTER TABLE app.Employees ADD CONSTRAINT DF_app_Employees_CanProcureAssets DEFAULT ((0)) FOR CanProcureAssets;
GO
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_app_Employees_EmployeeCode' AND object_id=OBJECT_ID('app.Employees'))
    CREATE UNIQUE INDEX UX_app_Employees_EmployeeCode ON app.Employees(EmployeeCode) WHERE EmployeeCode IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_app_Employees_BiometricCode' AND object_id=OBJECT_ID('app.Employees'))
    CREATE UNIQUE INDEX UX_app_Employees_BiometricCode ON app.Employees(BiometricCode) WHERE BiometricCode IS NOT NULL;
GO
