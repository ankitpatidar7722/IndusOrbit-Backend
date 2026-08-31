using Microsoft.Data.SqlClient;

namespace Indus360.Api.Data;

/// <summary>Thin factory for opened SqlConnections (Dapper, no EF).</summary>
public sealed class Db
{
    private readonly string _default;
    private readonly string? _control;
    private readonly string? _tms;
    private readonly string? _appDb;
    private readonly string? _keyline;
    private readonly string _mode;

    public Db(IConfiguration cfg)
    {
        // Local ↔ Server switch. "DatabaseMode" = "Local" (your machine) or "Server"
        // (the hosted IndusOrbit DB). Each name is looked up under the matching section
        // first — ConnectionStrings:{Mode}:{Name} — and falls back to a flat
        // ConnectionStrings:{Name} for anything shared by both modes (control / keyline).
        // Can also be overridden at runtime with a DatabaseMode env var.
        var mode = cfg["DatabaseMode"]?.Trim();
        if (string.IsNullOrWhiteSpace(mode)) mode = "Local";
        _mode = mode;

        string? Conn(string name) =>
            cfg[$"ConnectionStrings:{mode}:{name}"] ?? cfg.GetConnectionString(name);

        _default = Conn("Default")
                   ?? throw new InvalidOperationException($"ConnectionStrings:{mode}:Default (or ConnectionStrings:Default) not configured.");
        // The central "Indus" control-plane DB (subscriptions / company master catalog).
        _control = Conn("IndusControl");
        // The Task Management (Point Management module) database — migrated TMS app.
        _tms = Conn("IndusTaskManagement");
        // The IndusAppDB HR database (Employees) — used for employee login.
        _appDb = Conn("IndusApp");
        // The IndusEnterpriseKeyline lookup DB (Change-Request module/sub-module dropdowns).
        _keyline = Conn("IndusKeyline");
    }

    /// <summary>The active DatabaseMode ("Local" or "Server").</summary>
    public string Mode => _mode;

    /// <summary>Resolved connection strings by logical name — for the /api/health/db diagnostic.</summary>
    public IReadOnlyList<(string Name, string? ConnectionString)> ConfiguredConnections =>
        new (string Name, string? ConnectionString)[]
        {
            ("Default", _default),
            ("IndusControl", _control),
            ("IndusTaskManagement", _tms),
            ("IndusApp", _appDb),
            ("IndusKeyline", _keyline),
        };

    /// <summary>Open a connection to the Indus360App application database.</summary>
    public async Task<SqlConnection> OpenAsync()
    {
        var c = new SqlConnection(_default);
        await c.OpenAsync();
        return c;
    }

    /// <summary>Open a connection to the central "Indus" control-plane database.</summary>
    public async Task<SqlConnection> OpenControlAsync()
    {
        if (string.IsNullOrWhiteSpace(_control))
            throw new InvalidOperationException("ConnectionStrings:IndusControl not configured.");
        var c = new SqlConnection(_control);
        await c.OpenAsync();
        return c;
    }

    /// <summary>Open a connection to the IndusTaskManagement database (Point Management module).</summary>
    public async Task<SqlConnection> OpenTmsAsync()
    {
        if (string.IsNullOrWhiteSpace(_tms))
            throw new InvalidOperationException("ConnectionStrings:IndusTaskManagement not configured.");
        var c = new SqlConnection(_tms);
        await c.OpenAsync();
        return c;
    }

    /// <summary>Open a connection to the IndusAppDB HR database (Employees — employee login).</summary>
    public async Task<SqlConnection> OpenAppDbAsync()
    {
        if (string.IsNullOrWhiteSpace(_appDb))
            throw new InvalidOperationException("ConnectionStrings:IndusApp not configured.");
        var c = new SqlConnection(_appDb);
        await c.OpenAsync();
        return c;
    }
}
