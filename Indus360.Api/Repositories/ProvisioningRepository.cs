using System.Text;
using Dapper;
using Indus360.Api.Models;
using Microsoft.Data.SqlClient;

namespace Indus360.Api.Repositories;

/// <summary>
/// Client-provisioning logic cloned from BulkImport's CompanySubscriptionService:
/// create a new client DB by BACKUP/RESTORE of a template, then write Company / Branch /
/// Production masters into it and finalize credentials.
///
/// IMPORTANT: SetupDatabaseAsync performs real BACKUP/RESTORE against SQL Server and is
/// intended for production onboarding. The connection helpers reuse the IndusControl
/// credentials (server user/password) rather than hard-coding them.
/// </summary>
public sealed class ProvisioningRepository
{
    private readonly IConfiguration _cfg;
    private readonly string _controlCs;

    public ProvisioningRepository(IConfiguration cfg)
    {
        _cfg = cfg;
        _controlCs = cfg.GetConnectionString("IndusControl")
                     ?? throw new InvalidOperationException("ConnectionStrings:IndusControl not configured.");
    }

    private SqlConnectionStringBuilder ControlCsb => new(_controlCs);
    private SqlConnection ControlConnection() => new(_controlCs);

    /// <summary>A connection to a target server's master DB (reuses control-DB credentials).</summary>
    private SqlConnection MasterConnection(string server)
    {
        var c = ControlCsb;
        var b = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = "master",
            UserID = c.UserID,
            Password = c.Password,
            TrustServerCertificate = true,
            Encrypt = c.Encrypt,
            ConnectTimeout = 30,
        };
        return new SqlConnection(b.ConnectionString);
    }

    /// <summary>A connection to a client DB from its stored connection string.</summary>
    private static SqlConnection ClientConnection(string connString)
    {
        var b = new SqlConnectionStringBuilder(connString) { TrustServerCertificate = true, ConnectTimeout = 30 };
        return new SqlConnection(b.ConnectionString);
    }

    // ── 1. Servers ────────────────────────────────────────────────
    public List<string> GetServers()
    {
        var configured = _cfg.GetSection("Provisioning:Servers").Get<string[]>();
        if (configured is { Length: > 0 }) return configured.ToList();
        return new List<string> { ControlCsb.DataSource }; // default: the control server
    }

    // ── 2. Backup (template) databases for an application ─────────
    public async Task<List<string>> GetBackupDatabasesAsync(string applicationName)
    {
        using var c = ControlConnection();
        var rows = await c.QueryAsync<string>(
            "SELECT DatabaseName FROM dbo.DynamicBackupDatabase WHERE ApplicationName=@applicationName",
            new { applicationName });
        return rows.ToList();
    }

    // ── 3. Setup database (BACKUP source template → RESTORE new client DB) ──
    public async Task<SetupDatabaseResponse> SetupDatabaseAsync(SetupDatabaseRequest req)
    {
        var fail = (string m) => new SetupDatabaseResponse { Success = false, Message = m };

        if (string.IsNullOrWhiteSpace(req.Server) || string.IsNullOrWhiteSpace(req.ApplicationName)
            || string.IsNullOrWhiteSpace(req.DatabaseName) || string.IsNullOrWhiteSpace(req.BackupDatabaseName))
            return fail("Server, Application, Database Name and Backup Database Name are required.");

        var sourceDb = req.BackupDatabaseName!;
        var targetServer = req.Server;
        var newDbName = req.DatabaseName.Trim();

        // A) resolve the source server that hosts the template DB
        string? sourceServer;
        using (var indus = ControlConnection())
        {
            sourceServer = await indus.ExecuteScalarAsync<string>(
                "SELECT ServerName FROM dbo.DynamicBackupDatabase WHERE ApplicationName=@a AND DatabaseName=@d",
                new { a = req.ApplicationName, d = sourceDb });
        }
        if (string.IsNullOrEmpty(sourceServer))
            return fail("Could not find matching backup database server.");

        // B) if the template already exists on the target server, restore locally (avoid UNC)
        if (sourceServer != targetServer)
        {
            await using var tconn = MasterConnection(targetServer);
            await tconn.OpenAsync();
            var exists = await tconn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.databases WHERE name=@n", new { n = sourceDb });
            if (exists > 0) sourceServer = targetServer;
        }

        string backupPath;
        // C) BACKUP the template on the source server
        await using (var sconn = MasterConnection(sourceServer))
        {
            await sconn.OpenAsync();
            var backupDir = await sconn.ExecuteScalarAsync<string>(
                "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS NVARCHAR(500))")
                ?? await sconn.ExecuteScalarAsync<string>(
                    "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS NVARCHAR(500))")
                ?? @"C:\Temp\";
            if (!backupDir.EndsWith("\\")) backupDir += "\\";
            backupPath = $"{backupDir}{newDbName}_{DateTime.Now:yyyyMMddHHmmss}.bak";

            await sconn.ExecuteAsync(
                $"BACKUP DATABASE [{sourceDb}] TO DISK = N'{backupPath}' WITH FORMAT, INIT, SKIP, NOUNLOAD, STATS = 10",
                commandTimeout: 600);
        }

        // D) cross-server → build UNC admin-share path
        var restorePath = backupPath;
        var isCrossServer = sourceServer != targetServer;
        if (isCrossServer)
        {
            var sourceIp = sourceServer!.Replace(",1433", "").Replace(",1434", "");
            var drive = backupPath.Substring(0, 1);
            var afterDrive = backupPath.Substring(2);
            restorePath = $@"\\{sourceIp}\{drive}${afterDrive}";
        }

        // E) RESTORE on the target server
        await using (var tconn = MasterConnection(targetServer))
        {
            await tconn.OpenAsync();

            var exists = await tconn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.databases WHERE name=@n", new { n = newDbName });
            if (exists > 0) return fail($"Database '{newDbName}' already exists on {targetServer}.");

            if (isCrossServer)
            {
                try
                {
                    await tconn.ExecuteAsync($"EXEC master.dbo.xp_fileexist N'{restorePath}'", commandTimeout: 15);
                }
                catch
                {
                    return fail($"Backup file not reachable at {restorePath}. Ensure SMB (445), the admin C$ share, and the SQL service account's network access are available.");
                }
            }

            var files = (await tconn.QueryAsync($"RESTORE FILELISTONLY FROM DISK = N'{restorePath}'", commandTimeout: 120)).ToList();

            var dataPath = await tconn.ExecuteScalarAsync<string>(
                "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS NVARCHAR(500))") ?? @"C:\SQLData\";
            var logPath = await tconn.ExecuteScalarAsync<string>(
                "SELECT CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS NVARCHAR(500))") ?? dataPath;
            if (!dataPath.EndsWith("\\")) dataPath += "\\";
            if (!logPath.EndsWith("\\")) logPath += "\\";

            var move = new StringBuilder();
            var dataIdx = 0;
            foreach (IDictionary<string, object> f in files)
            {
                var logical = f["LogicalName"]?.ToString();
                var type = f["Type"]?.ToString();
                string physical;
                if (type == "L")
                {
                    physical = $"{logPath}{newDbName}_log.ldf";
                }
                else
                {
                    var suffix = dataIdx == 0 ? ".mdf" : $"_{dataIdx}.ndf";
                    physical = $"{dataPath}{newDbName}{suffix}";
                    dataIdx++;
                }
                move.Append($", MOVE N'{logical}' TO N'{physical}'");
            }

            await tconn.ExecuteAsync(
                $"RESTORE DATABASE [{newDbName}] FROM DISK = N'{restorePath}' WITH REPLACE{move}",
                commandTimeout: 600);
        }

        // F) best-effort cleanup of the .bak (non-critical; xp_cmdshell may be disabled)
        try
        {
            await using var clean = MasterConnection(sourceServer);
            await clean.OpenAsync();
            await clean.ExecuteAsync($"EXEC master.dbo.xp_cmdshell 'del \"{backupPath}\"'", commandTimeout: 30);
        }
        catch { /* non-critical */ }

        // G) build & return the new client connection string
        var csb = ControlCsb;
        var clientCs = $"Data Source={targetServer};Initial Catalog={newDbName};User ID={csb.UserID};Password={csb.Password};Persist Security Info=True;MultipleActiveResultSets=True;Encrypt=True;TrustServerCertificate=True";
        return new SetupDatabaseResponse
        {
            Success = true,
            Message = $"Database '{newDbName}' created on {targetServer}.",
            ConnectionString = clientCs,
            DatabaseName = newDbName,
            Server = targetServer,
            ApplicationName = req.ApplicationName,
            ClientName = req.ClientName,
        };
    }

    // ── 4. Company Master (client DB) ─────────────────────────────
    public async Task<CompanyMasterResponse> SaveCompanyMasterAsync(CompanyMasterRequest r)
    {
        await using var c = ClientConnection(r.ConnectionString);
        await c.OpenAsync();
        var exists = await c.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM CompanyMaster WHERE CompanyID=@CompanyID", new { r.CompanyID });

        if (exists > 0)
        {
            await c.ExecuteAsync(@"
                UPDATE CompanyMaster SET
                    CompanyName=@CompanyName, Address1=@Address1, Address2=@Address2, Address3=@Address3,
                    City=@City, State=@State, Country=@Country, Pincode=@Pincode,
                    ContactNO=@ContactNO, MobileNO=@MobileNO, Email=@Email, Website=@Website,
                    StateTinNo=@StateTinNo, CINNo=@CINNo, ProductionUnitAddress=@ProductionUnitAddress,
                    Address=@Address, GSTIN=@GSTIN, ProductionUnitName=@ProductionUnitName, PAN=@PAN
                WHERE CompanyID=@CompanyID", r);
        }
        else
        {
            await c.ExecuteAsync(@"
                SET IDENTITY_INSERT CompanyMaster ON;
                INSERT INTO CompanyMaster (CompanyID, CompanyName, Address1, Address2, Address3,
                    City, State, Country, Pincode, ContactNO, MobileNO, Email, Website, StateTinNo, CINNo,
                    ProductionUnitAddress, Address, GSTIN, ProductionUnitName, PAN)
                VALUES (@CompanyID, @CompanyName, @Address1, @Address2, @Address3,
                    @City, @State, @Country, @Pincode, @ContactNO, @MobileNO, @Email, @Website, @StateTinNo, @CINNo,
                    @ProductionUnitAddress, @Address, @GSTIN, @ProductionUnitName, @PAN);
                SET IDENTITY_INSERT CompanyMaster OFF;", r);
        }
        return new CompanyMasterResponse { Success = true, Message = "Company Master saved.", CompanyID = r.CompanyID };
    }

    // ── 5. Branch Master (client DB) ──────────────────────────────
    public async Task<SimpleResult> SaveBranchMasterAsync(BranchMasterRequest r)
    {
        await using var c = ClientConnection(r.ConnectionString);
        await c.OpenAsync();

        var companyId = (r.CompanyID is null or 0)
            ? await c.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster ORDER BY CompanyID DESC") ?? 2
            : r.CompanyID.Value;
        var mailing = string.IsNullOrWhiteSpace(r.MailingName) ? r.BranchName : r.MailingName;

        var p = new
        {
            r.BranchID, r.BranchName, MailingName = mailing, r.Address1, r.Address2, r.Address3, r.Address,
            r.City, r.District, r.State, r.Country, r.Pincode, r.MobileNo, r.Email, r.StateTinNo, r.GSTIN,
            CompanyID = companyId,
        };

        var exists = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM BranchMaster WHERE BranchID=@BranchID", p);
        if (exists > 0)
        {
            await c.ExecuteAsync(@"
                UPDATE BranchMaster SET
                    BranchName=@BranchName, MailingName=@MailingName, Address1=@Address1, Address2=@Address2,
                    Address3=@Address3, Address=@Address, City=@City, District=@District, State=@State,
                    Country=@Country, Pincode=@Pincode, MobileNo=@MobileNo, Email=@Email, StateTinNo=@StateTinNo,
                    GSTIN=@GSTIN, CompanyID=@CompanyID
                WHERE BranchID=@BranchID", p);
        }
        else
        {
            await c.ExecuteAsync(@"
                SET IDENTITY_INSERT BranchMaster ON;
                INSERT INTO BranchMaster (BranchID, BranchName, MailingName, Address1, Address2, Address3, Address,
                    City, District, State, Country, Pincode, MobileNo, Email, StateTinNo, GSTIN, CompanyID)
                VALUES (@BranchID, @BranchName, @MailingName, @Address1, @Address2, @Address3, @Address,
                    @City, @District, @State, @Country, @Pincode, @MobileNo, @Email, @StateTinNo, @GSTIN, @CompanyID);
                SET IDENTITY_INSERT BranchMaster OFF;", p);
        }
        return new SimpleResult { Success = true, Message = "Branch Master saved." };
    }

    // ── 6. Production Unit (client DB) ────────────────────────────
    public async Task<SimpleResult> SaveProductionUnitAsync(ProductionUnitRequest r)
    {
        await using var c = ClientConnection(r.ConnectionString);
        await c.OpenAsync();

        var companyId = await c.ExecuteScalarAsync<int?>("SELECT TOP 1 CompanyID FROM CompanyMaster ORDER BY CompanyID DESC") ?? 2;
        var branchId = await c.ExecuteScalarAsync<int?>("SELECT TOP 1 BranchID FROM BranchMaster ORDER BY BranchID DESC") ?? 1;
        var userId = await c.ExecuteScalarAsync<int?>("SELECT TOP 1 UserID FROM UserMaster WHERE UserName='admin'") ?? 1;
        var maxNo = await c.ExecuteScalarAsync<int?>("SELECT ISNULL(MAX(MaxProductionUnitNo),0) FROM ProductionUnitMaster") ?? 0;
        var newNo = maxNo + 1;

        await c.ExecuteAsync("DELETE FROM ProductionUnitMaster");
        await c.ExecuteAsync(@"
            INSERT INTO ProductionUnitMaster
                (ProductionUnitName, Address, City, State, GSTNo, Pincode, Country,
                 CompanyID, BranchID, UserID, IsLocked, IsDeletedTransaction,
                 MaxProductionUnitNo, ProductionUnitCode, PAN)
            VALUES
                (@ProductionUnitName, @Address, @City, @State, @GSTNo, @Pincode, @Country,
                 @CompanyID, @BranchID, @UserID, 0, 0,
                 @MaxProductionUnitNo, @ProductionUnitCode, @PAN)",
            new
            {
                r.ProductionUnitName, r.Address, r.City, r.State, r.GSTNo, r.Pincode, r.Country,
                CompanyID = companyId, BranchID = branchId, UserID = userId,
                MaxProductionUnitNo = newNo, ProductionUnitCode = $"PU{newNo:D5}", r.PAN,
            });

        return new SimpleResult { Success = true, Message = "Production Unit saved." };
    }

    // ── 7. Complete setup (finalize + return credentials) ─────────
    public async Task<CompleteSetupResponse> CompleteSetupAsync(CompleteSetupRequest r)
    {
        string adminUser = "admin", adminPass = "";
        await using (var c = ClientConnection(r.ConnectionString))
        {
            await c.OpenAsync();
            await c.ExecuteAsync(
                "UPDATE UserMaster SET City=@City, State=@State, Country=@Country WHERE UserName='admin'",
                new { r.City, r.State, r.Country });
            var row = await c.QuerySingleOrDefaultAsync(
                "SELECT UserName, Password FROM UserMaster WHERE UserName='admin'");
            if (row is not null)
            {
                adminUser = (string?)row.UserName ?? "admin";
                adminPass = (string?)row.Password ?? "";
            }
        }

        using var indus = ControlConnection();
        var sub = await indus.QuerySingleOrDefaultAsync(
            "SELECT CompanyUserID, Password FROM dbo.Indus_Company_Authentication_For_Web_Modules WHERE CompanyUserID=@id",
            new { id = r.CompanyUserID });
        if (sub is null)
            return new CompleteSetupResponse { Success = false, Message = "Subscription not found." };

        return new CompleteSetupResponse
        {
            Success = true,
            Message = "Setup completed successfully!",
            CompanyUserID = (string?)sub.CompanyUserID,
            Password = (string?)sub.Password,
            UserName = adminUser,
            UserPassword = adminPass,
        };
    }
}
