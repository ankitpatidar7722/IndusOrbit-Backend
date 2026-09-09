using Dapper;
using Indus360.Api.Data;

namespace Indus360.Api.Repositories;

/// <summary>One "sent to client" record for a master Excel template.</summary>
public sealed class TemplateSentRow
{
    public string TemplateGroup { get; set; } = "";
    public string TemplateName { get; set; } = "";
    public DateTime SentAt { get; set; }
    public string? SentByName { get; set; }
}

/// <summary>
/// "Sent to Client" status for master Excel templates (app.TemplateSentStatus). The templates
/// themselves are files on disk (see <see cref="Controllers.MasterTemplatesController"/>); this table
/// records, PER CLIENT, which template was last emailed to them and when — so each client's template
/// list can show a "Sent to Client · date" badge. Created on first use, so a publish-only deploy
/// needs no manual SQL.
/// </summary>
public sealed class TemplateStatusRepository
{
    private readonly Db _db;
    public TemplateStatusRepository(Db db) => _db = db;

    private static bool _ready;
    private static readonly SemaphoreSlim _gate = new(1, 1);

    private async Task EnsureAsync(Microsoft.Data.SqlClient.SqlConnection c)
    {
        if (_ready) return;
        await _gate.WaitAsync();
        try
        {
            if (_ready) return;
            await c.ExecuteAsync(@"
IF OBJECT_ID('app.TemplateSentStatus','U') IS NULL
BEGIN
    CREATE TABLE app.TemplateSentStatus (
        Id            BIGINT IDENTITY(1,1) CONSTRAINT PK_TemplateSentStatus PRIMARY KEY,
        ClientCode    NVARCHAR(100) NOT NULL,
        TemplateGroup NVARCHAR(200) NOT NULL CONSTRAINT DF_TemplateSentStatus_Group DEFAULT '',
        TemplateName  NVARCHAR(300) NOT NULL,
        SentAt        DATETIME2 NOT NULL CONSTRAINT DF_TemplateSentStatus_Sent DEFAULT SYSUTCDATETIME(),
        SentByID      INT NULL
    );
    CREATE UNIQUE INDEX UX_TemplateSentStatus_Key ON app.TemplateSentStatus(ClientCode, TemplateGroup, TemplateName);
END");
            _ready = true;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Every template this client has been emailed (group + name + when + by whom).</summary>
    public async Task<List<TemplateSentRow>> GetForClientAsync(string clientCode)
    {
        await using var c = await _db.OpenAsync();
        await EnsureAsync(c);
        var rows = await c.QueryAsync<TemplateSentRow>(@"
            SELECT s.TemplateGroup, s.TemplateName, s.SentAt, u.FullName AS SentByName
            FROM app.TemplateSentStatus s
            LEFT JOIN app.Users u ON u.UserId = s.SentByID
            WHERE s.ClientCode = @clientCode", new { clientCode });
        return rows.AsList();
    }

    /// <summary>Upsert "sent to this client" for each (group, name) template.</summary>
    public async Task MarkSentAsync(string clientCode, int? sentBy, IEnumerable<(string group, string name)> items)
    {
        await using var c = await _db.OpenAsync();
        await EnsureAsync(c);
        foreach (var (group, name) in items)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            await c.ExecuteAsync(@"
UPDATE app.TemplateSentStatus SET SentAt=SYSUTCDATETIME(), SentByID=@sentBy
 WHERE ClientCode=@clientCode AND TemplateGroup=@group AND TemplateName=@name;
IF @@ROWCOUNT = 0
   INSERT INTO app.TemplateSentStatus (ClientCode, TemplateGroup, TemplateName, SentByID)
   VALUES (@clientCode, @group, @name, @sentBy);",
                new { clientCode, group = group ?? "", name, sentBy });
        }
    }
}
