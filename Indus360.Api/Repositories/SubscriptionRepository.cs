using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;

namespace Indus360.Api.Repositories;

/// <summary>
/// Reads client subscriptions from the central "Indus" control DB
/// (dbo.Indus_Company_Authentication_For_Web_Modules) — cloned from BulkImport's
/// Company Subscription feature. Phase 1: list + detail.
/// </summary>
public sealed class SubscriptionRepository
{
    private readonly Db _db;
    private readonly Services.CacheService _cache;
    public SubscriptionRepository(Db db, Services.CacheService cache) { _db = db; _cache = cache; }

    private const string Table = "dbo.Indus_Company_Authentication_For_Web_Modules";

    // Cached reads all hit the REMOTE control DB — the heaviest queries under concurrent load.
    // 60s TTL + explicit bust on every subscription write (Create/Update/SoftDelete), so a change
    // made in Indus 360 shows immediately and only external changes wait out the short TTL.
    private const string KAll = "subs:all";
    private const string KStats = "subs:stats";
    private const string KAppUrls = "subs:appurls";
    private const string KDropdown = "subs:dropdown";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    private void BustCaches() => _cache.Remove(KAll, KStats, KAppUrls, KDropdown);

    /// <summary>Distinct non-empty ApplicationBaseURL values — for the Application URL dropdown.</summary>
    public async Task<IEnumerable<string>> GetAppBaseUrlsAsync() =>
        await _cache.GetOrCreateAsync(KAppUrls, Ttl, async () =>
        {
            using var c = await _db.OpenControlAsync();
            return (await c.QueryAsync<string>($@"
            SELECT DISTINCT ApplicationBaseURL FROM {Table}
            WHERE NULLIF(LTRIM(RTRIM(ApplicationBaseURL)),'') IS NOT NULL
            ORDER BY ApplicationBaseURL")).ToList();
        });

    public async Task<IEnumerable<SubscriptionCard>> GetAllAsync() =>
        await _cache.GetOrCreateAsync(KAll, Ttl, async () =>
        {
            using var c = await _db.OpenControlAsync();
            return (await c.QueryAsync<SubscriptionCard>($@"
            SELECT CompanyUserID, CompanyUniqueCode, CompanyName, CompanyCode, ApplicationName, ApplicationVersion,
                   SubscriptionStatus, StatusDescription, SubscriptionStatusMessage, Address, City, State, Country,
                   GSTIN, Email, Mobile, FromDate, ToDate, PaymentDueDate, LoginAllowed, LastLoginDateTime, CloudSubscriptionStatus,
                   -- Database name parsed from Conn_String (Initial Catalog=...) so the UI can distinguish
                   -- same-named clients. The full connection string (with password) is never exposed.
                   CASE WHEN CHARINDEX('Initial Catalog=', Conn_String) > 0
                        THEN SUBSTRING(Conn_String,
                               CHARINDEX('Initial Catalog=', Conn_String) + 16,
                               CHARINDEX(';', Conn_String + ';', CHARINDEX('Initial Catalog=', Conn_String) + 16)
                                 - (CHARINDEX('Initial Catalog=', Conn_String) + 16))
                        ELSE NULL END AS DatabaseName
            FROM {Table}
            WHERE ISNULL(IsActive,1) = 1
            ORDER BY CompanyName")).ToList();
        });

    /// <summary>True if the user's role is admin. Admins see ALL clients (bypass Project Assignment).
    /// Queries the LOCAL app.Users (not the prod control DB).</summary>
    public async Task<bool> IsAdminAsync(long userId)
    {
        await using var c = await _db.OpenAsync();
        var role = await c.ExecuteScalarAsync<string?>("SELECT TOP 1 Role FROM app.Users WHERE UserId=@userId", new { userId });
        return string.Equals(role?.Trim(), "admin", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<SubscriptionDto?> GetByKeyAsync(string companyUserId)
    {
        using var c = await _db.OpenControlAsync();
        return await c.QuerySingleOrDefaultAsync<SubscriptionDto>($@"
            SELECT CompanyUserID, Password, Conn_String, CompanyName, ApplicationName, ApplicationVersion,
                   DataBaseLocation, LastLoginDateTime, IsActive, Country, State, City, ApplicationBaseURL,
                   CompanyCode, CompanyUniqueCode, MaxCompanyUniqueCode, FromDate, ToDate, FYear, PaymentDueDate,
                   SubscriptionStatus, StatusDescription, SubscriptionStatusMessage, LoginAllowed, UserLimit, GSTIN,
                   LatestVersion, Email, Mobile, Address, IsMessageActive, MessageDurationValue, MessageDurationType,
                   CloudSubscriptionStatus, CloudFromDate, CloudToDate, CloudPaymentDueDate,
                   ERPSubscriptionPeriod, CloudSubscriptionPeriod
            FROM {Table}
            WHERE CompanyUserID = @companyUserId",
            new { companyUserId });
    }

    /// <summary>Next client code (IA00009-style) + numeric max.</summary>
    public async Task<(string code, int max)> GetNextCodeAsync()
    {
        using var c = await _db.OpenControlAsync();
        // Avoid TRY_CONVERT (unsupported on older SQL Server / low compat level):
        // take MAX of the numeric part of IA##### codes, ignoring non-numeric ones.
        var max = await c.ExecuteScalarAsync<int>($@"
            SELECT ISNULL(MAX(CAST(REPLACE(CompanyUniqueCode,'IA','') AS int)), 0)
            FROM {Table}
            WHERE CompanyUniqueCode LIKE 'IA%'
              AND LEN(REPLACE(CompanyUniqueCode,'IA','')) > 0
              AND REPLACE(CompanyUniqueCode,'IA','') NOT LIKE '%[^0-9]%'");
        var next = max + 1;
        return ($"IA{next:D5}", next);
    }

    public async Task<bool> ExistsAsync(string companyUserId)
    {
        using var c = await _db.OpenControlAsync();
        return await c.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM {Table} WHERE CompanyUserID=@companyUserId", new { companyUserId }) > 0;
    }

    public async Task CreateAsync(SubscriptionSaveRequest r)
    {
        using var c = await _db.OpenControlAsync();
        await c.ExecuteAsync($@"
            INSERT INTO {Table}
              (CompanyUserID, Password, Conn_String, CompanyName, ApplicationName, ApplicationVersion,
               Country, State, City, CompanyCode, CompanyUniqueCode, MaxCompanyUniqueCode, FromDate, ToDate, FYear,
               PaymentDueDate, SubscriptionStatus, StatusDescription, SubscriptionStatusMessage, LoginAllowed, UserLimit, GSTIN,
               Email, Mobile, Address, ApplicationBaseURL, IsActive, IsMessageActive, MessageDurationValue, MessageDurationType,
               CloudSubscriptionStatus, CloudFromDate, CloudToDate, CloudPaymentDueDate,
               ERPSubscriptionPeriod, CloudSubscriptionPeriod)
            VALUES
              (@CompanyUserID, @Password, @Conn_String, @CompanyName, @ApplicationName, @ApplicationVersion,
               @Country, @State, @City, @CompanyCode, @CompanyUniqueCode, @MaxCompanyUniqueCode, @FromDate, @ToDate, @FYear,
               @PaymentDueDate, @SubscriptionStatus, @StatusDescription, @SubscriptionStatusMessage, @LoginAllowed, @UserLimit, @Gstin,
               @Email, @Mobile, @Address, @ApplicationBaseURL, 1, @IsMessageActive, @MessageDurationValue, @MessageDurationType,
               @CloudSubscriptionStatus, @CloudFromDate, @CloudToDate, @CloudPaymentDueDate,
               @ErpSubscriptionPeriod, @CloudSubscriptionPeriod)", r);
        BustCaches(); // new subscription → refresh cached list/stats/dropdown/urls immediately
    }

    public async Task<int> UpdateAsync(SubscriptionSaveRequest r)
    {
        var key = string.IsNullOrWhiteSpace(r.OriginalCompanyUserID) ? r.CompanyUserID : r.OriginalCompanyUserID;
        using var c = await _db.OpenControlAsync();
        var rows = await c.ExecuteAsync($@"
            UPDATE {Table} SET
              CompanyUserID=@CompanyUserID, Password=@Password, Conn_String=@Conn_String, CompanyName=@CompanyName,
              ApplicationName=@ApplicationName, ApplicationVersion=@ApplicationVersion, Country=@Country, State=@State,
              City=@City, CompanyCode=@CompanyCode, CompanyUniqueCode=@CompanyUniqueCode, FromDate=@FromDate, ToDate=@ToDate,
              FYear=@FYear, PaymentDueDate=@PaymentDueDate, SubscriptionStatus=@SubscriptionStatus,
              StatusDescription=@StatusDescription, SubscriptionStatusMessage=@SubscriptionStatusMessage,
              LoginAllowed=@LoginAllowed, UserLimit=@UserLimit, GSTIN=@Gstin, Email=@Email, Mobile=@Mobile, Address=@Address, ApplicationBaseURL=@ApplicationBaseURL,
              IsMessageActive=@IsMessageActive, MessageDurationValue=@MessageDurationValue, MessageDurationType=@MessageDurationType,
              CloudSubscriptionStatus=@CloudSubscriptionStatus, CloudFromDate=@CloudFromDate, CloudToDate=@CloudToDate, CloudPaymentDueDate=@CloudPaymentDueDate,
              ERPSubscriptionPeriod=@ErpSubscriptionPeriod, CloudSubscriptionPeriod=@CloudSubscriptionPeriod
            WHERE CompanyUserID=@Key",
            new
            {
                r.CompanyUserID, r.Password, r.Conn_String, r.CompanyName, r.ApplicationName, r.ApplicationVersion,
                r.Country, r.State, r.City, r.CompanyCode, r.CompanyUniqueCode, r.FromDate, r.ToDate, r.FYear,
                r.PaymentDueDate, r.SubscriptionStatus, r.StatusDescription, r.SubscriptionStatusMessage,
                r.LoginAllowed, r.UserLimit, r.Gstin, r.Email, r.Mobile, r.Address, r.ApplicationBaseURL, r.IsMessageActive, r.MessageDurationValue,
                r.MessageDurationType, r.CloudSubscriptionStatus, r.CloudFromDate, r.CloudToDate, r.CloudPaymentDueDate,
                r.ErpSubscriptionPeriod, r.CloudSubscriptionPeriod,
                Key = key,
            });
        BustCaches(); // edited subscription → refresh cached list/stats/dropdown/urls immediately
        return rows;
    }

    /// <summary>
    /// Validate the acting user against the LOCAL app.Users login — the SAME credentials the
    /// Indus 360 login uses (Email + PasswordHash). i.e. the logged-in user re-enters their own
    /// Indus 360 email + password to confirm the delete (not the remote CompanyWebUser table).
    /// </summary>
    public async Task<bool> ValidateActorAsync(string userName, string password)
    {
        await using var c = await _db.OpenAsync(); // local app DB (app.Users — same store login uses)
        return await c.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM app.Users
              WHERE Email = @u AND PasswordHash = @p
                AND IsActive = 1 AND ISNULL(IsDeletedTransaction,0) = 0",
            new { u = userName, p = password }) > 0;
    }

    /// <summary>Soft delete (IsActive=0). Returns rows affected.</summary>
    public async Task<int> SoftDeleteAsync(string companyUserId)
    {
        using var c = await _db.OpenControlAsync();
        var rows = await c.ExecuteAsync(
            $"UPDATE {Table} SET IsActive=0 WHERE CompanyUserID=@companyUserId", new { companyUserId });
        BustCaches(); // deleted subscription → refresh cached list/stats/dropdown/urls immediately
        return rows;
    }

    /// <summary>Non-Desktop clients for the copy-modules target dropdown.</summary>
    public async Task<IEnumerable<ClientDropdownItem>> GetClientDropdownAsync() =>
        await _cache.GetOrCreateAsync(KDropdown, Ttl, async () =>
        {
            using var c = await _db.OpenControlAsync();
            return (await c.QueryAsync<ClientDropdownItem>($@"
            SELECT CompanyName, CompanyUserID, ApplicationName
            FROM {Table}
            WHERE ISNULL(IsActive,1) = 1 AND ISNULL(ApplicationName,'') <> 'Desktop'
            ORDER BY CompanyName")).ToList();
        });

    /// <summary>Summary counts for the Customers header stats.</summary>
    public async Task<(int total, int active, int expired)> GetStatsAsync() =>
        await _cache.GetOrCreateAsync(KStats, Ttl, async () =>
        {
            using var c = await _db.OpenControlAsync();
            var row = await c.QuerySingleAsync($@"
            SELECT
                COUNT(*) AS Total,
                SUM(CASE WHEN SubscriptionStatus = 'Active'  THEN 1 ELSE 0 END) AS Active,
                SUM(CASE WHEN SubscriptionStatus = 'Expired' THEN 1 ELSE 0 END) AS Expired
            FROM {Table}
            WHERE ISNULL(IsActive,1) = 1");
            return ((int)row.Total, (int)(row.Active ?? 0), (int)(row.Expired ?? 0));
        });

    // ---------------- Message Format templates (dbo.MessageFormatMaster, Indus control DB) ----------------
    private const string MsgTable = "dbo.MessageFormatMaster";

    public async Task<IEnumerable<MessageFormatDto>> GetMessageFormatsAsync()
    {
        using var c = await _db.OpenControlAsync();
        return await c.QueryAsync<MessageFormatDto>($@"
            SELECT MessageID, MessageTitle, MessageContent, ISNULL(IsActive,1) AS IsActive
            FROM {MsgTable}
            WHERE ISNULL(IsDeletedTransaction,0) = 0
            ORDER BY MessageTitle");
    }

    public async Task<long> CreateMessageFormatAsync(MessageFormatSaveRequest r)
    {
        using var c = await _db.OpenControlAsync();
        return await c.ExecuteScalarAsync<long>($@"
            INSERT INTO {MsgTable} (MessageTitle, MessageContent, IsActive, CreatedDate, IsDeletedTransaction)
            OUTPUT INSERTED.MessageID
            VALUES (@MessageTitle, @MessageContent, @IsActive, GETDATE(), 0)",
            new { r.MessageTitle, r.MessageContent, IsActive = r.IsActive ?? true });
    }

    public async Task<int> UpdateMessageFormatAsync(MessageFormatSaveRequest r)
    {
        using var c = await _db.OpenControlAsync();
        return await c.ExecuteAsync($@"
            UPDATE {MsgTable}
            SET MessageTitle=@MessageTitle, MessageContent=@MessageContent, IsActive=@IsActive, ModifiedDate=GETDATE()
            WHERE MessageID=@MessageID",
            new { r.MessageID, r.MessageTitle, r.MessageContent, IsActive = r.IsActive ?? true });
    }

    public async Task<int> DeleteMessageFormatAsync(long messageId)
    {
        using var c = await _db.OpenControlAsync();
        return await c.ExecuteAsync(
            $"UPDATE {MsgTable} SET IsDeletedTransaction=1, ModifiedDate=GETDATE() WHERE MessageID=@messageId",
            new { messageId });
    }
}
