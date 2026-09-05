using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;
using Microsoft.Data.SqlClient;

namespace Indus360.Api.Repositories;

/// <summary>
/// Gathers the live auto-fill values for a client's Sign-Off document. Reads the control DB
/// (client master), the client's own product DB (create date, contact person, in-scope modules)
/// and the app DB (milestone Res. Person = implementation engineer + revision → version).
/// All queries are READ-ONLY. Client-DB reads are best-effort: if that DB is unreachable
/// (e.g. it lives on a private network) the client-derived fields are simply left blank.
/// </summary>
public sealed class SignoffDataRepository
{
    private readonly Db _db;
    public SignoffDataRepository(Db db) => _db = db;

    private const string ControlTable = "dbo.Indus_Company_Authentication_For_Web_Modules";

    private static SqlConnection Client(string cs)
        => new(new SqlConnectionStringBuilder(cs) { TrustServerCertificate = true, ConnectTimeout = 15 }.ConnectionString);

    // Sign-off scope rows (doc module) → the ModuleHeadName expected in the client's ModuleMaster.
    // Matches the red-marked instructions in the source Word document. "Job Card" maps to
    // "ProductionWorkOrder" in the client DB.
    private static readonly string[] ScopeHeads =
    {
        "Artwork Management", "Sales Enquiry", "Estimation", "Order Booking", "ProductionWorkOrder",
        "Inventory", "Scheduling", "Production", "Quality Control", "Dispatch", "Machine Maintenance",
    };

    public async Task<SignoffData> GetAsync(string companyUserId)
    {
        var d = new SignoffData();
        string? connStr = null, uniqueCode = null;

        // ── 1) control DB — client master ───────────────────────────────
        using (var c = await _db.OpenControlAsync())
        {
            var row = await c.QuerySingleOrDefaultAsync(
                $@"SELECT CompanyUniqueCode, CompanyName, ApplicationName, Conn_String, Address, City
                   FROM {ControlTable} WHERE CompanyUserID = @companyUserId", new { companyUserId });
            if (row is not null)
            {
                uniqueCode = ((string?)row.CompanyUniqueCode)?.Trim();
                d.CompanyName = ((string?)row.CompanyName ?? "").Trim();
                d.ErpProduct = MapProduct((string?)row.ApplicationName);
                d.Address = ((string?)row.Address ?? "").Trim();
                d.City = ((string?)row.City ?? "").Trim();
                connStr = (string?)row.Conn_String;
            }
        }

        d.DocumentCode = string.IsNullOrWhiteSpace(uniqueCode) ? "IA-ERP-CLS-" : $"IA-ERP-CLS-{uniqueCode}";
        var today = DateTime.Now.ToString("dd-MMM-yyyy");
        d.DocumentDate = today;
        d.GoLiveDate = today;
        d.ProjectCompletionDate = today;

        // ── 2) client product DB — best-effort (may be unreachable) ─────
        if (!string.IsNullOrWhiteSpace(connStr))
        {
            try
            {
                await using var cc = Client(connStr!);
                await cc.OpenAsync();

                var created = await cc.ExecuteScalarAsync<DateTime?>(
                    "SELECT create_date FROM sys.databases WHERE name = DB_NAME()");
                if (created.HasValue)
                {
                    d.ProjectStartDate = created.Value.ToString("dd-MMM-yyyy");
                    d.ProjectStartDateIso = created.Value.ToString("yyyy-MM-dd");
                }

                d.ContactPerson = await GetContactPersonAsync(cc);

                var heads = (await cc.QueryAsync<string>(
                    "SELECT DISTINCT ModuleHeadName FROM ModuleMaster WHERE ISNULL(IsDeletedTransaction,0)=0 AND ModuleHeadName IS NOT NULL"))
                    .Select(h => (h ?? "").Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                d.InScopeModules = ScopeHeads.Where(h => heads.Contains(h)).ToList();
            }
            catch { /* client DB unreachable / schema differs — leave those fields blank */ }
        }

        // ── 3) app DB — implementation engineer (+mobile) & version ─────
        var docCode = string.IsNullOrWhiteSpace(uniqueCode) ? companyUserId : uniqueCode!;
        try
        {
            await using var app = await _db.OpenAsync();

            if (!string.IsNullOrWhiteSpace(uniqueCode))
            {
                var eng = await app.ExecuteScalarAsync<string?>(
                    @"SELECT TOP 1 ResPerson FROM app.Milestones
                      WHERE ClientCode = @code AND ISNULL(IsDeletedTransaction,0)=0
                        AND NULLIF(LTRIM(RTRIM(ResPerson)),'') IS NOT NULL
                      ORDER BY SortOrder, Id", new { code = uniqueCode });
                d.ImplementationEngineer = (eng ?? "").Trim();

                if (!string.IsNullOrWhiteSpace(d.ImplementationEngineer))
                    d.ImplementationEngineerMobile = (await app.ExecuteScalarAsync<string?>(
                        @"SELECT TOP 1 Mobile FROM app.Users
                          WHERE (FullName = @n OR Email = @n) AND ISNULL(IsDeletedTransaction,0)=0
                            AND NULLIF(LTRIM(RTRIM(Mobile)),'') IS NOT NULL",
                        new { n = d.ImplementationEngineer }) ?? "").Trim();
            }

            var rev = await app.ExecuteScalarAsync<int?>(
                "SELECT Revision FROM app.ClientDocuments WHERE ClientCode = @code AND DocType = 'SignOff'",
                new { code = docCode }) ?? 0;
            d.Version = $"1.{rev}";

            // §8 Support Email options — distinct emails of active Support-role app users.
            try
            {
                d.SupportEmails = (await app.QueryAsync<string>(
                    @"SELECT DISTINCT Email FROM app.Users
                      WHERE Role = 'Support' AND ISNULL(IsActive,0) = 1 AND ISNULL(IsDeletedTransaction,0) = 0
                        AND NULLIF(LTRIM(RTRIM(Email)),'') IS NOT NULL
                      ORDER BY Email")).ToList();
            }
            catch { /* leave empty — cell falls back to the default support email */ }

            // Name/email → mobile map (active users): lets §8 Support Contact follow §2
            // Implementation Engineer client-side as the user edits the name.
            try
            {
                var rows = await app.QueryAsync(
                    @"SELECT FullName, Email, Mobile FROM app.Users
                      WHERE ISNULL(IsActive,0) = 1 AND ISNULL(IsDeletedTransaction,0) = 0
                        AND NULLIF(LTRIM(RTRIM(Mobile)),'') IS NOT NULL");
                foreach (var r in rows)
                {
                    string mob = ((string?)r.Mobile ?? "").Trim();
                    if (mob.Length == 0) continue;
                    string n = ((string?)r.FullName ?? "").Trim().ToLowerInvariant();
                    string em = ((string?)r.Email ?? "").Trim().ToLowerInvariant();
                    if (n.Length > 0 && !d.UserMobiles.ContainsKey(n)) d.UserMobiles[n] = mob;
                    if (em.Length > 0 && !d.UserMobiles.ContainsKey(em)) d.UserMobiles[em] = mob;
                }
            }
            catch { /* leave empty — Support Contact simply won't auto-derive */ }
        }
        catch { d.Version = "1.0"; }

        return d;
    }

    /// <summary>Find a contact-person-ish column on the client's CompanyMaster and read its first
    /// non-empty value. Schema-agnostic (the column name varies by product), best-effort.</summary>
    private static async Task<string> GetContactPersonAsync(SqlConnection cc)
    {
        try
        {
            var cols = (await cc.QueryAsync<string>(
                "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID('CompanyMaster')")).ToList();
            if (cols.Count == 0) return "";

            bool Has(string c, string needle) => c.Replace("_", "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
            var col = cols.FirstOrDefault(c => Has(c, "concerningperson"))   // Indus ERP CompanyMaster column
                   ?? cols.FirstOrDefault(c => Has(c, "contactperson"))
                   ?? cols.FirstOrDefault(c => Has(c, "concerning"))
                   ?? cols.FirstOrDefault(c => Has(c, "proprietor"))
                   ?? cols.FirstOrDefault(c => Has(c, "ownername"))
                   ?? cols.FirstOrDefault(c => Has(c, "authorizedperson"))
                   ?? cols.FirstOrDefault(c => Has(c, "contactname"));
            if (col is null) return "";

            var val = await cc.ExecuteScalarAsync<string?>(
                $"SELECT TOP 1 [{col}] FROM CompanyMaster WHERE NULLIF(LTRIM(RTRIM([{col}])),'') IS NOT NULL");
            return (val ?? "").Trim();
        }
        catch { return ""; }
    }

    private static string MapProduct(string? app) => (app ?? "").Trim().ToLowerInvariant() switch
    {
        "estimoprime" or "desktop" => "EstimoPrime ERP",
        "printudeerp" => "Printude ERP",
        "multiunit" => "Indus Multi-Unit ERP",
        "" => "Indus Print ERP",
        _ => app!.Trim(),
    };
}
