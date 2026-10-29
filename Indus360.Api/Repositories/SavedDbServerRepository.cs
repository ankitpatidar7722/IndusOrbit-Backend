using Dapper;
using Indus360.Api.Data;
using Microsoft.AspNetCore.DataProtection;

namespace Indus360.Api.Repositories;

/// <summary>
/// Per-user history of SQL servers used on Admin → Database Backup ("By Server"): server name + login,
/// and — only when the user ticks "Remember password" — the password, encrypted with ASP.NET Data
/// Protection. The password is never returned to the browser; the backend decrypts it itself when
/// the page asks to "use the saved password". Table is lazy-created (app.SavedDbServers).
/// </summary>
public sealed class SavedDbServerRepository
{
    private readonly Db _db;
    private readonly IDataProtector _protector;

    public SavedDbServerRepository(Db db, IDataProtectionProvider dp)
    {
        _db = db;
        _protector = dp.CreateProtector("Indus360.DatabaseBackup.SavedServerPassword.v1");
    }

    public sealed record SavedServer(string Server, string? LoginUser, bool HasPassword, DateTime LastUsed);

    private static bool _ready;
    private static readonly SemaphoreSlim _gate = new(1, 1);

    private static async Task EnsureAsync(Microsoft.Data.SqlClient.SqlConnection c)
    {
        if (_ready) return;
        await _gate.WaitAsync();
        try
        {
            if (_ready) return;
            await c.ExecuteAsync(@"
IF OBJECT_ID('app.SavedDbServers','U') IS NULL
BEGIN
    CREATE TABLE app.SavedDbServers (
        Id          INT IDENTITY(1,1) CONSTRAINT PK_SavedDbServers PRIMARY KEY,
        UserId      INT NOT NULL,
        ServerName  NVARCHAR(200) NOT NULL,
        LoginUser   NVARCHAR(128) NULL,
        PasswordEnc NVARCHAR(MAX) NULL,
        LastUsed    DATETIME2 NOT NULL CONSTRAINT DF_SavedDbServers_LastUsed DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_SavedDbServers_User_Server ON app.SavedDbServers(UserId, ServerName);
END");
            _ready = true;
        }
        finally { _gate.Release(); }
    }

    public async Task<List<SavedServer>> ListAsync(int userId)
    {
        await using var c = await _db.OpenAsync();
        await EnsureAsync(c);
        var rows = await c.QueryAsync<SavedServer>(
            @"SELECT ServerName AS Server, LoginUser, CAST(CASE WHEN PasswordEnc IS NULL THEN 0 ELSE 1 END AS bit) AS HasPassword, LastUsed
              FROM app.SavedDbServers WHERE UserId = @userId ORDER BY LastUsed DESC", new { userId });
        return rows.ToList();
    }

    /// <summary>Saved login + decrypted password for one server, or null if none / undecryptable.</summary>
    public async Task<(string? LoginUser, string? Password)?> GetCredentialsAsync(int userId, string server)
    {
        await using var c = await _db.OpenAsync();
        await EnsureAsync(c);
        var row = await c.QueryFirstOrDefaultAsync<(string? LoginUser, string? PasswordEnc)>(
            "SELECT LoginUser, PasswordEnc FROM app.SavedDbServers WHERE UserId = @userId AND ServerName = @server",
            new { userId, server });
        if (row.PasswordEnc == null) return null;
        try { return (row.LoginUser, _protector.Unprotect(row.PasswordEnc)); }
        catch { return null; }   // key ring changed → the user just types the password again
    }

    /// <summary>
    /// Record a successful connection. remember=true stores the (encrypted) password; false clears any
    /// stored one but keeps the server in the history. A null password with remember=true keeps the
    /// existing saved password (used when the page connected with the saved one).
    /// </summary>
    public async Task UpsertAsync(int userId, string server, string loginUser, string? password, bool remember)
    {
        string? enc = remember && !string.IsNullOrEmpty(password) ? _protector.Protect(password) : null;
        await using var c = await _db.OpenAsync();
        await EnsureAsync(c);
        await c.ExecuteAsync(@"
UPDATE app.SavedDbServers
   SET LoginUser = @loginUser,
       PasswordEnc = CASE WHEN @remember = 0 THEN NULL WHEN @enc IS NULL THEN PasswordEnc ELSE @enc END,
       LastUsed = SYSUTCDATETIME()
 WHERE UserId = @userId AND ServerName = @server;
IF @@ROWCOUNT = 0
    INSERT INTO app.SavedDbServers (UserId, ServerName, LoginUser, PasswordEnc) VALUES (@userId, @server, @loginUser, @enc);",
            new { userId, server, loginUser, enc, remember });
    }

    public async Task DeleteAsync(int userId, string server)
    {
        await using var c = await _db.OpenAsync();
        await EnsureAsync(c);
        await c.ExecuteAsync("DELETE FROM app.SavedDbServers WHERE UserId = @userId AND ServerName = @server", new { userId, server });
    }
}
