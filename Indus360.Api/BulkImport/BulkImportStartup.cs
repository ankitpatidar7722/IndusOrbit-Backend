using Microsoft.Data.SqlClient;

namespace Indus360.Api.BulkImportSupport;

/// <summary>
/// BulkImport's ORIGINAL startup auto-migrations, extracted verbatim from its Program.cs.
/// Idempotent IF-NOT-EXISTS DDL against the tenant DB + the shared Indus control DB
/// (13.200.122.70/Indus). Tables already exist in production, so this is normally a no-op.
/// Gated by config "BulkImport:RunStartupMigrations" so it can be turned off entirely.
/// </summary>
public static class BulkImportStartup
{
    public static void RunStartupMigrations(WebApplication app)
    {
using (var scope = app.Services.CreateScope())
{
    try
    {
        var conn = scope.ServiceProvider.GetRequiredService<SqlConnection>();
        conn.Open();
        System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] DB Init Started\n");

        // 1. Ensure Table Exists (Basic Schema)
        var createTableCmd = @"
            IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CompanyMaster]') AND type in (N'U'))
            BEGIN
                CREATE TABLE [CompanyMaster] (
                    [CompanyId] INT PRIMARY KEY IDENTITY(1,1),
                    [CompanyName] NVARCHAR(200) NOT NULL,
                    [Address] NVARCHAR(MAX),
                    -- Core columns only to avoid validation errors if schema drifts
                    [IsActive] BIT DEFAULT 1
                );
            END";
        using (var cmd = new SqlCommand(createTableCmd, conn)) cmd.ExecuteNonQuery();

        // 2. Ensure Columns Exist (One by one or batched safe ALTERs)
        var columns = new[]
        {
            "Phone NVARCHAR(50)", "Email NVARCHAR(100)", "Website NVARCHAR(100)", "GSTIN NVARCHAR(50)",
            "IsGstApplicable BIT DEFAULT 0 NOT NULL", "IsEinvoiceApplicable BIT DEFAULT 0 NOT NULL",
            "IsInternalApprovalRequired BIT DEFAULT 0 NOT NULL", "IsRequisitionApproval BIT DEFAULT 0 NOT NULL",
            "IsPOApprovalRequired BIT DEFAULT 0 NOT NULL", "IsInvoiceApprovalRequired BIT DEFAULT 0 NOT NULL",
            "IsGRNApprovalRequired BIT DEFAULT 0 NOT NULL", "JobScheduleReleaseRequired BIT DEFAULT 0 NOT NULL",
            "IsSalesOrderApprovalRequired BIT DEFAULT 0 NOT NULL", "IsJobReleaseFeatureRequired BIT DEFAULT 0 NOT NULL",
            "ShowPlanUptoWastagePerc BIT DEFAULT 0 NOT NULL", "ByPassCostApproval BIT DEFAULT 0 NOT NULL",
            "IsDeletedTransaction BIT DEFAULT 0 NOT NULL"
        };

        foreach (var colDef in columns)
        {
            var colName = colDef.Split(' ')[0];
            var alterCmd = $@"
                IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'{colName}' AND Object_ID = Object_ID(N'CompanyMaster'))
                BEGIN
                    ALTER TABLE CompanyMaster ADD {colDef};
                END";
            using (var cmd = new SqlCommand(alterCmd, conn)) cmd.ExecuteNonQuery();
        }

        // 3. Ensure Default Data (using dynamic SQL to verify columns exist before insert, or just skipping if table populated)
        // Check if empty
        var countCmd = "SELECT COUNT(*) FROM CompanyMaster";
        using (var cmd = new SqlCommand(countCmd, conn))
        {
            int count = (int)cmd.ExecuteScalar();
            if (count == 0)
            {
                var insertCmd = @"
                    INSERT INTO [CompanyMaster] (CompanyName, Address, Phone, Email, Website, GSTIN, IsActive)
                    VALUES ('Indus Technologies', '123 Business Park, Tech City', '+1 234 567 8900', 'info@industech.com', 'www.industech.com', 'GST123456789', 1)";
                using (var iCmd = new SqlCommand(insertCmd, conn)) iCmd.ExecuteNonQuery();
            }
        }
        
        conn.Close();
        System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] DB Init Completed\n");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"DB Init Error: {ex.Message}");
        System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] DB Init Error: {ex.Message}\n");
    }

    try
    {
        var conn = scope.ServiceProvider.GetRequiredService<SqlConnection>();
        conn.Open();
        System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] Module Master Init Started\n");

        var moduleCols = new[]
        {
            "ModuleHeadDisplayName NVARCHAR(200)", 
            "ModuleHeadDisplayOrder INT DEFAULT 0", 
            "ModuleDisplayOrder INT DEFAULT 0",
            "SetGroupIndex INT DEFAULT 0",
            "Description NVARCHAR(MAX)"
        };

        foreach (var colDef in moduleCols)
        {
            var colName = colDef.Split(' ')[0];
            var alterCmd = $@"
                IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ModuleMaster]') AND type in (N'U'))
                BEGIN
                    IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'{colName}' AND Object_ID = Object_ID(N'ModuleMaster'))
                    BEGIN
                        ALTER TABLE ModuleMaster ADD {colDef};
                    END
                END";
            using (var cmd = new SqlCommand(alterCmd, conn)) cmd.ExecuteNonQuery();
        }
        
        conn.Close();
    }
    catch(Exception ex) {
         Console.WriteLine($"Module Init Error: {ex.Message}");
    }

    // SparePartMaster Schema Migration
    try
    {
        var conn = scope.ServiceProvider.GetRequiredService<SqlConnection>();
        conn.Open();
        System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] SparePartMaster Init Started\n");

        // Check if SparePartMaster table exists
        var tableExistsCmd = "SELECT COUNT(*) FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[SparePartMaster]') AND type in (N'U')";
        using (var cmd = new SqlCommand(tableExistsCmd, conn))
        {
            int tableExists = (int)cmd.ExecuteScalar();
            if (tableExists > 0)
            {
                // Add required columns for import functionality
                var sparePartCols = new[]
                {
                    "HSNCode NVARCHAR(50) NULL",
                    "HSNGroup NVARCHAR(100) NULL",
                    "SupplierReference NVARCHAR(100) NULL",
                    "StockRefCode NVARCHAR(50) NULL",
                    "PurchaseOrderQuantity DECIMAL(18, 2) DEFAULT 0 NULL",
                    "SparePartGroup NVARCHAR(100) NULL",
                    "SparePartType NVARCHAR(100) NULL"
                };

                foreach (var colDef in sparePartCols)
                {
                    var colName = colDef.Split(' ')[0];
                    var alterCmd = $@"
                        IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'{colName}' AND Object_ID = Object_ID(N'SparePartMaster'))
                        BEGIN
                            ALTER TABLE SparePartMaster ADD {colDef};
                        END";
                    using (var cmd2 = new SqlCommand(alterCmd, conn))
                    {
                        cmd2.ExecuteNonQuery();
                    }
                }
                System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] SparePartMaster columns check/add completed\n");

                // Widen string columns that may have been created with a small nvarchar size
                var widenCols = new[]
                {
                    "SparePartName",
                    "SparePartGroup",
                    "SparePartType",
                    "HSNGroup",
                    "SupplierReference",
                    "StockRefCode",
                    "Narration"
                };
                foreach (var col in widenCols)
                {
                    var widenCmd = $@"
                        IF COL_LENGTH('SparePartMaster', '{col}') IS NOT NULL
                        AND COL_LENGTH('SparePartMaster', '{col}') < 500
                        BEGIN
                            ALTER TABLE SparePartMaster ALTER COLUMN {col} NVARCHAR(500) NULL;
                        END";
                    using (var cmd3 = new SqlCommand(widenCmd, conn))
                        cmd3.ExecuteNonQuery();
                }
            }
        }

        conn.Close();
        System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] SparePartMaster Init Completed\n");
    }
    catch(Exception ex) {
         Console.WriteLine($"SparePartMaster Init Error: {ex.Message}");
         System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] SparePartMaster Init Error: {ex.Message}\n");
    }

    // LedgerMaster Schema Migration (Consignee support)
    try
    {
        var conn = scope.ServiceProvider.GetRequiredService<SqlConnection>();
        conn.Open();
        System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] LedgerMaster Init Started\n");

        // Check if LedgerMaster table exists
        var tableExistsCmd = "SELECT COUNT(*) FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[LedgerMaster]') AND type in (N'U')";
        using (var cmd = new SqlCommand(tableExistsCmd, conn))
        {
            int tableExists = (int)cmd.ExecuteScalar();
            if (tableExists > 0)
            {
                // Add RefClientID column for Consignee support
                var alterCmd = @"
                    IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'RefClientID' AND Object_ID = Object_ID(N'LedgerMaster'))
                    BEGIN
                        ALTER TABLE LedgerMaster ADD RefClientID INT NULL;
                    END";
                using (var cmd2 = new SqlCommand(alterCmd, conn))
                {
                    cmd2.ExecuteNonQuery();
                }
                System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] LedgerMaster RefClientID column check/add completed\n");
            }
        }

        conn.Close();
        System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] LedgerMaster Init Completed\n");
    }
    catch(Exception ex) {
         Console.WriteLine($"LedgerMaster Init Error: {ex.Message}");
         System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] LedgerMaster Init Error: {ex.Message}\n");
    }


    // CountryStateMaster Schema and Seeding
    try
    {
        var conn = scope.ServiceProvider.GetRequiredService<SqlConnection>();
        conn.Open();
        System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] CountryStateMaster Init Started\n");

        // 1. Ensure Table Exists
        var createTableCmd = @"
            IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CountryStateMaster]') AND type in (N'U'))
            BEGIN
                CREATE TABLE [CountryStateMaster] (
                    [CountryStateID] INT PRIMARY KEY IDENTITY(1,1),
                    [Country] NVARCHAR(100) NOT NULL,
                    [State] NVARCHAR(100) NOT NULL
                );
            END";
        using (var cmd = new SqlCommand(createTableCmd, conn)) cmd.ExecuteNonQuery();

        // 2. Ensure Seed Data (India + States)
        var countCmd = "SELECT COUNT(*) FROM CountryStateMaster";
        using (var cmd = new SqlCommand(countCmd, conn))
        {
            int count = (int)cmd.ExecuteScalar();
            if (count == 0)
            {
                // Basic list of India states
                var insertCmd = @"
                    INSERT INTO [CountryStateMaster] (Country, State) VALUES 
                    ('India', 'Andhra Pradesh'),
                    ('India', 'Arunachal Pradesh'),
                    ('India', 'Assam'),
                    ('India', 'Bihar'),
                    ('India', 'Chhattisgarh'),
                    ('India', 'Goa'),
                    ('India', 'Gujarat'),
                    ('India', 'Haryana'),
                    ('India', 'Himachal Pradesh'),
                    ('India', 'Jharkhand'),
                    ('India', 'Karnataka'),
                    ('India', 'Kerala'),
                    ('India', 'Madhya Pradesh'),
                    ('India', 'Maharashtra'),
                    ('India', 'Manipur'),
                    ('India', 'Meghalaya'),
                    ('India', 'Mizoram'),
                    ('India', 'Nagaland'),
                    ('India', 'Odisha'),
                    ('India', 'Punjab'),
                    ('India', 'Rajasthan'),
                    ('India', 'Sikkim'),
                    ('India', 'Tamil Nadu'),
                    ('India', 'Telangana'),
                    ('India', 'Tripura'),
                    ('India', 'Uttar Pradesh'),
                    ('India', 'Uttarakhand'),
                    ('India', 'West Bengal'),
                    ('India', 'Delhi'),
                    ('India', 'Jammu and Kashmir'),
                    ('India', 'Ladakh'),
                    ('India', 'Puducherry');";
                using (var iCmd = new SqlCommand(insertCmd, conn)) iCmd.ExecuteNonQuery();
                System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] CountryStateMaster Seeded with India states\n");
            }
        }

        conn.Close();
        System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] CountryStateMaster Init Completed\n");
    }
    catch(Exception ex) {
         Console.WriteLine($"CountryStateMaster Init Error: {ex.Message}");
         System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] CountryStateMaster Init Error: {ex.Message}\n");
    }

    // ItemMaster Schema Migration (BF column for REEL support + INK columns)
    try
    {
        var conn = scope.ServiceProvider.GetRequiredService<SqlConnection>();
        conn.Open();
        System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] ItemMaster Init Started\n");

        // Check if ItemMaster table exists
        var tableExistsCmd = "SELECT COUNT(*) FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ItemMaster]') AND type in (N'U')";
        using (var cmd = new SqlCommand(tableExistsCmd, conn))
        {
            int tableExists = (int)cmd.ExecuteScalar();
            if (tableExists > 0)
            {
                // Add columns for REEL and INK & ADDITIVES support
                var itemMasterCols = new[]
                {
                    "BF NVARCHAR(100) NULL",
                    "InkColour NVARCHAR(100) NULL",
                    "PantoneCode NVARCHAR(50) NULL",
                    "PurchaseOrderQuantity DECIMAL(18, 2) DEFAULT 0 NULL",
                    "IsDeletedTransaction BIT DEFAULT 0 NOT NULL"
                };

                foreach (var colDef in itemMasterCols)
                {
                    var colName = colDef.Split(' ')[0];
                    var alterCmd = $@"
                        IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'{colName}' AND Object_ID = Object_ID(N'ItemMaster'))
                        BEGIN
                            ALTER TABLE ItemMaster ADD {colDef};
                        END";
                    using (var cmd2 = new SqlCommand(alterCmd, conn))
                    {
                        cmd2.ExecuteNonQuery();
                    }
                }
                System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] ItemMaster columns check/add completed\n");
            }
        }

        conn.Close();
        System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] ItemMaster Init Completed\n");
    }
    catch(Exception ex) {
         Console.WriteLine($"ItemMaster Init Error: {ex.Message}");
         System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] ItemMaster Init Error: {ex.Message}\n");
    }

    // IndusToolModuleMaster + CompanyModuleAuthority — runs against IndusDB (central admin tables)
    try
    {
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var indusConnStr = config.GetConnectionString("IndusConnection");
        if (!string.IsNullOrEmpty(indusConnStr))
        {
            using var indusConn = new SqlConnection(indusConnStr);
            indusConn.Open();

            var createModuleMasterCmd = @"
                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[IndusToolModuleMaster]') AND type in (N'U'))
                BEGIN
                    CREATE TABLE [IndusToolModuleMaster] (
                        [ModuleID]     INT           PRIMARY KEY IDENTITY(1,1),
                        [ModuleName]   NVARCHAR(100) NOT NULL,
                        [ModulePath]   NVARCHAR(200) NOT NULL,
                        [ModuleIcon]   NVARCHAR(100) NOT NULL DEFAULT '',
                        [DisplayOrder] INT           NOT NULL DEFAULT 0
                    );
                END";
            using (var cmd = new SqlCommand(createModuleMasterCmd, indusConn)) cmd.ExecuteNonQuery();

            var createCompanyAuthCmd = @"
                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CompanyModuleAuthority]') AND type in (N'U'))
                BEGIN
                    CREATE TABLE [CompanyModuleAuthority] (
                        [ID]            INT           PRIMARY KEY IDENTITY(1,1),
                        [CompanyUserID] NVARCHAR(100) NOT NULL,
                        [ModuleID]      INT           NOT NULL
                    );
                END";
            using (var cmd = new SqlCommand(createCompanyAuthCmd, indusConn)) cmd.ExecuteNonQuery();

            int moduleCount;
            using (var cmd = new SqlCommand("SELECT COUNT(*) FROM IndusToolModuleMaster", indusConn))
                moduleCount = (int)cmd.ExecuteScalar();

            if (moduleCount == 0)
            {
                var seedCmd = @"
                    INSERT INTO IndusToolModuleMaster (ModuleName, ModulePath, ModuleIcon, DisplayOrder) VALUES
                    ('Dashboard',               '/',                       'LayoutDashboard', 1),
                    ('Import Master',           '/import-master',          'Upload',          2),
                    ('Stock Upload',            '/stock-upload',           'PackageOpen',     3),
                    ('Company Master',          '/company-master',         'Building2',       4),
                    ('Module Authority',        '/dynamic-module',         'Layers',          5),
                    ('Content Authority',       '/content-authority',      'BookOpen',        6),
                    ('ERP Transaction Delete',  '/erp-transaction-delete', 'Trash2',          7),
                    ('Key Line Generator',      '/keyline-generator',      'PenTool',         8)";
                using (var cmd = new SqlCommand(seedCmd, indusConn)) cmd.ExecuteNonQuery();
            }

            // Idempotent insert for new modules added after initial seed
            var newModules = new[]
            {
                ("New Module Addition", "/module-authority", "PlusSquare", 9)
            };
            foreach (var (name, path, icon, order) in newModules)
            {
                var insertCmd = $@"
                    IF NOT EXISTS (SELECT 1 FROM IndusToolModuleMaster WHERE ModulePath = '{path}')
                    BEGIN
                        INSERT INTO IndusToolModuleMaster (ModuleName, ModulePath, ModuleIcon, DisplayOrder)
                        VALUES ('{name}', '{path}', '{icon}', {order});
                    END";
                using (var cmd = new SqlCommand(insertCmd, indusConn)) cmd.ExecuteNonQuery();
            }

            indusConn.Close();
            System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] IndusToolModuleMaster Init Completed\n");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"IndusToolModuleMaster Init Error: {ex.Message}");
        System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] IndusToolModuleMaster Init Error: {ex.Message}\n");
    }

    // ── Subscription Plan Catalog (global Indus DB) ───────────────────────────
    // Feature → FeaturePlan (tiers) → sub-features (stored on the plan as JSON,
    // validated against the feature's FeatureSubFeature master list). Plans defined
    // once here are applied to companies' tenant DBs (CompanyFeatureSubscription).
    // Idempotent — safe to run on every startup.
    try
    {
        var catalogConfig = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var indusConnStr = catalogConfig.GetConnectionString("IndusConnection");
        if (!string.IsNullOrEmpty(indusConnStr))
        {
            using var indusConn = new SqlConnection(indusConnStr);
            indusConn.Open();
            System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] Plan Catalog Init Started\n");

            var createFeatureCmd = @"
                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Feature]') AND type in (N'U'))
                BEGIN
                    CREATE TABLE [Feature] (
                        [FeatureID]   INT           PRIMARY KEY IDENTITY(1,1),
                        [FeatureCode] NVARCHAR(20)  NOT NULL,
                        [FeatureName] NVARCHAR(100) NOT NULL,
                        [IsActive]    BIT           NOT NULL DEFAULT 1,
                        [CreatedAt]   DATETIME      NOT NULL DEFAULT GETUTCDATE(),
                        [UpdatedAt]   DATETIME      NULL,
                        CONSTRAINT UQ_Feature_Code UNIQUE ([FeatureCode])
                    );
                END";
            using (var cmd = new SqlCommand(createFeatureCmd, indusConn)) cmd.ExecuteNonQuery();

            // 2-table model: no FeatureSubFeature master table. Sub-features + the
            // customer-facing card fields live inline on FeaturePlan.
            var createPlanCmd = @"
                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[FeaturePlan]') AND type in (N'U'))
                BEGIN
                    CREATE TABLE [FeaturePlan] (
                        [PlanID]          INT            PRIMARY KEY IDENTITY(1,1),
                        [FeatureID]       INT            NOT NULL,
                        [PlanName]        NVARCHAR(100)  NOT NULL,
                        [PlanDisplayName] NVARCHAR(150)  NULL,
                        [PlanCode]        NVARCHAR(50)   NULL,
                        [CompanyUserID]   NVARCHAR(50)   NULL,   -- NULL = global (all companies); value = override for that company only (matches Indus_Company_Authentication_For_Web_Modules.CompanyUserID)
                        [CompanyName]     NVARCHAR(200)  NULL,   -- display label only (the company's name); never used for matching
                        [BillingCycle]    NVARCHAR(10)   NOT NULL DEFAULT 'MONTHLY',
                        [UnitPrice]       DECIMAL(12,2)  NOT NULL DEFAULT 0,
                        [AnnualPrice]     DECIMAL(12,2)  NULL,
                        [PerUser]         BIT            NOT NULL DEFAULT 0,
                        [PerUserNote]     NVARCHAR(150)  NULL,
                        [Blurb]           NVARCHAR(300)  NULL,
                        [Highlight]       BIT            NOT NULL DEFAULT 0,
                        [Badge]           NVARCHAR(50)   NULL,
                        [FeaturesJson]    NVARCHAR(MAX)  NULL,
                        [SubFeaturesJson] NVARCHAR(MAX)  NULL,
                        [RazorpayPlanId]  NVARCHAR(64)   NULL,
                        [IsActive]        BIT            NOT NULL DEFAULT 1,
                        [CreatedAt]       DATETIME       NOT NULL DEFAULT GETUTCDATE(),
                        [UpdatedAt]       DATETIME       NULL,
                        CONSTRAINT FK_Plan_Feature FOREIGN KEY ([FeatureID]) REFERENCES [Feature]([FeatureID])
                    );
                END
                -- Idempotent column adds for DBs created before the card fields existed.
                IF COL_LENGTH('FeaturePlan','PlanDisplayName') IS NULL ALTER TABLE [FeaturePlan] ADD [PlanDisplayName] NVARCHAR(150) NULL;
                IF COL_LENGTH('FeaturePlan','PlanCode')        IS NULL ALTER TABLE [FeaturePlan] ADD [PlanCode]        NVARCHAR(50)  NULL;
                IF COL_LENGTH('FeaturePlan','AnnualPrice')     IS NULL ALTER TABLE [FeaturePlan] ADD [AnnualPrice]     DECIMAL(12,2) NULL;
                IF COL_LENGTH('FeaturePlan','PerUserNote')     IS NULL ALTER TABLE [FeaturePlan] ADD [PerUserNote]     NVARCHAR(150) NULL;
                IF COL_LENGTH('FeaturePlan','Blurb')           IS NULL ALTER TABLE [FeaturePlan] ADD [Blurb]           NVARCHAR(300) NULL;
                IF COL_LENGTH('FeaturePlan','Highlight')       IS NULL ALTER TABLE [FeaturePlan] ADD [Highlight]       BIT NOT NULL DEFAULT 0;
                IF COL_LENGTH('FeaturePlan','Badge')           IS NULL ALTER TABLE [FeaturePlan] ADD [Badge]           NVARCHAR(50)  NULL;
                IF COL_LENGTH('FeaturePlan','FeaturesJson')    IS NULL ALTER TABLE [FeaturePlan] ADD [FeaturesJson]    NVARCHAR(MAX) NULL;
                -- Per-company override keyed on the GLOBALLY-UNIQUE CompanyUserID (the company
                -- login id in Indus_Company_Authentication_For_Web_Modules). NULL = global.
                -- CompanyName is a display label only. NOTE: the earlier CompanyID(int) attempt
                -- was unsafe (CompanyID is per-tenant-DB, not unique here) — drop it if present.
                IF COL_LENGTH('FeaturePlan','CompanyUserID')   IS NULL ALTER TABLE [FeaturePlan] ADD [CompanyUserID]   NVARCHAR(50)  NULL;
                IF COL_LENGTH('FeaturePlan','CompanyName')     IS NULL ALTER TABLE [FeaturePlan] ADD [CompanyName]     NVARCHAR(200) NULL;
                IF COL_LENGTH('FeaturePlan','CompanyID')       IS NOT NULL ALTER TABLE [FeaturePlan] DROP COLUMN [CompanyID];";
            using (var cmd = new SqlCommand(createPlanCmd, indusConn)) cmd.ExecuteNonQuery();

            // ── Cloud Subscription columns on Indus_Company_Authentication_For_Web_Modules ──
            var cloudColsCmd = @"
                IF COL_LENGTH('Indus_Company_Authentication_For_Web_Modules','CloudFromDate')           IS NULL ALTER TABLE [Indus_Company_Authentication_For_Web_Modules] ADD [CloudFromDate]           DATE NULL;
                IF COL_LENGTH('Indus_Company_Authentication_For_Web_Modules','CloudToDate')             IS NULL ALTER TABLE [Indus_Company_Authentication_For_Web_Modules] ADD [CloudToDate]             DATE NULL;
                IF COL_LENGTH('Indus_Company_Authentication_For_Web_Modules','CloudPaymentDueDate')     IS NULL ALTER TABLE [Indus_Company_Authentication_For_Web_Modules] ADD [CloudPaymentDueDate]     DATE NULL;
                IF COL_LENGTH('Indus_Company_Authentication_For_Web_Modules','CloudSubscriptionStatus') IS NULL ALTER TABLE [Indus_Company_Authentication_For_Web_Modules] ADD [CloudSubscriptionStatus] NVARCHAR(50) NULL;";
            using (var cmd = new SqlCommand(cloudColsCmd, indusConn)) cmd.ExecuteNonQuery();

            // ── Seed existing reality (idempotent: each guarded by NOT EXISTS) ──
            // Features: Sahay, Email
            var seedFeatures = new[]
            {
                ("Sahay", "Sahay"),
                ("Email", "Email"),
            };
            foreach (var (code, name) in seedFeatures)
            {
                var ins = $@"
                    IF NOT EXISTS (SELECT 1 FROM Feature WHERE FeatureCode = '{code}')
                        INSERT INTO Feature (FeatureCode, FeatureName) VALUES ('{code}', '{name}');";
                using var cmd = new SqlCommand(ins, indusConn); cmd.ExecuteNonQuery();
            }


            // Current plans (matches the existing IndusWebApi ResolvePlan amounts)
            var seedPlans = new[]
            {
                ("Sahay", "Sahay Pro",      "MONTHLY", 8000m, true,  "[{\"key\":\"basic_qa\",\"label\":\"Business-data Q&A\",\"enabled\":true},{\"key\":\"auto_charts\",\"label\":\"Auto-charts & visualisation\",\"enabled\":true},{\"key\":\"dashboards\",\"label\":\"Dashboards\",\"enabled\":true},{\"key\":\"ai_insights\",\"label\":\"AI insights\",\"enabled\":true}]"),
                ("Email", "Email Standard", "MONTHLY", 1999m, false, "[{\"key\":\"scheduled\",\"label\":\"Scheduled emails\",\"enabled\":true}]"),
            };
            foreach (var (featCode, planName, cycle, price, perUser, subsJson) in seedPlans)
            {
                var ins = $@"
                    IF NOT EXISTS (
                        SELECT 1 FROM FeaturePlan p
                        JOIN Feature f ON f.FeatureID = p.FeatureID
                        WHERE f.FeatureCode = '{featCode}' AND p.PlanName = '{planName}' AND p.BillingCycle = '{cycle}')
                    INSERT INTO FeaturePlan (FeatureID, PlanName, BillingCycle, UnitPrice, PerUser, SubFeaturesJson)
                        SELECT FeatureID, '{planName}', '{cycle}', {price}, {(perUser ? 1 : 0)}, '{subsJson.Replace("'", "''")}'
                        FROM Feature WHERE FeatureCode = '{featCode}';";
                using var cmd = new SqlCommand(ins, indusConn); cmd.ExecuteNonQuery();
            }

            indusConn.Close();
            System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] Plan Catalog Init Completed\n");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Plan Catalog Init Error: {ex.Message}");
        System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] Plan Catalog Init Error: {ex.Message}\n");
    }

    // ItemMasterDetails Schema Migration
    try
    {
        var conn = scope.ServiceProvider.GetRequiredService<SqlConnection>();
        conn.Open();
        System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] ItemMasterDetails Init Started\n");

        var tableExistsCmd = "SELECT COUNT(*) FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ItemMasterDetails]') AND type in (N'U')";
        using (var cmd = new SqlCommand(tableExistsCmd, conn))
        {
            int tableExists = (int)cmd.ExecuteScalar();
            if (tableExists > 0)
            {
                var colDef = "IsDeletedTransaction BIT DEFAULT 0 NOT NULL";
                var colName = "IsDeletedTransaction";
                var alterCmd = $@"
                    IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'{colName}' AND Object_ID = Object_ID(N'ItemMasterDetails'))
                    BEGIN
                        ALTER TABLE ItemMasterDetails ADD {colDef};
                    END";
                using (var cmd2 = new SqlCommand(alterCmd, conn))
                {
                    cmd2.ExecuteNonQuery();
                }
                System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] ItemMasterDetails IsDeletedTransaction column check/add completed\n");
            }
        }
        conn.Close();
    }
    catch(Exception ex) {
         Console.WriteLine($"ItemMasterDetails Init Error: {ex.Message}");
         System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] ItemMasterDetails Init Error: {ex.Message}\n");
    }

    // ItemConsumptionDetail Schema Migration (IsDeletedTransaction column)
    try
    {
        var conn = scope.ServiceProvider.GetRequiredService<SqlConnection>();
        conn.Open();
        var tableExistsCmd = "SELECT COUNT(*) FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ItemConsumptionDetail]') AND type in (N'U')";
        using (var cmd = new SqlCommand(tableExistsCmd, conn))
        {
            int tableExists = (int)cmd.ExecuteScalar();
            if (tableExists > 0)
            {
                var alterCmd = @"
                    IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'IsDeletedTransaction' AND Object_ID = Object_ID(N'ItemConsumptionDetail'))
                    BEGIN
                        ALTER TABLE ItemConsumptionDetail ADD IsDeletedTransaction BIT NOT NULL DEFAULT 0;
                    END";
                using (var cmd2 = new SqlCommand(alterCmd, conn))
                {
                    cmd2.ExecuteNonQuery();
                }
                System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] ItemConsumptionDetail IsDeletedTransaction column check/add completed\n");
            }
        }
        conn.Close();
    }
    catch(Exception ex) {
        Console.WriteLine($"ItemConsumptionDetail Migration Error: {ex.Message}");
        System.IO.File.AppendAllText("debug_log.txt", $"[{DateTime.Now}] ItemConsumptionDetail Migration Error: {ex.Message}\n");
    }

    // NOTE: Feature subscriptions are now TENANT-LOCAL (Option A) — the
    // CompanyFeatureSubscription table and UserMaster.PremiumFeatures column live
    // in each company's OWN database and are created on-demand by
    // FeatureSubscriptionService.EnsureTenantSchemaAsync. No central (Indus) tables.
}
    }
}
