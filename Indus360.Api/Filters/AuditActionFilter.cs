using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;
using Indus360.Api.Repositories;
using Indus360.Api.Services;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Indus360.Api.Filters;

/// <summary>
/// Global audit filter — automatically records EVERY mutating request (POST / PUT / PATCH / DELETE)
/// across all controllers into app.AuditLog: who, what action, which module, the entity, from which
/// IP / browser / OS, when, the outcome, and a sanitized payload snapshot. Read requests are skipped
/// (too noisy); rich field-level diffs are added separately by IAuditService on key entities.
/// Best-effort — a logging failure never affects the request.
/// </summary>
public sealed class AuditActionFilter : IAsyncActionFilter
{
    private readonly AuditLogRepository _audit;
    private readonly Db _db;
    public AuditActionFilter(AuditLogRepository audit, Db db) { _audit = audit; _db = db; }

    private static readonly ConcurrentDictionary<int, string> _nameCache = new();

    // Noisy / internal endpoints we never want in the trail.
    private static readonly string[] SkipContains =
        { "heartbeat", "negotiate", "/hub", "/audit-log", "push/subscribe", "push/unsubscribe", "/health",
          // login + session tracking record their own rich audit entries (see Auth/SessionsController)
          "/auth/login", "employee-auth/login", "/sessions/" };

    // Read-only endpoints that happen to be POST (they take a client connection string in the body) but
    // mutate nothing — listing a client's modules / catalog when the Module Settings modal opens. Logging
    // these as "Created Module Settings" floods the trail, so skip them. Matched on the path tail so the
    // real mutations ("/modules/settings/save", "/modules/client-module/create") are NOT caught.
    private static readonly string[] ReadOnlyPostSuffixes =
        { "/modules/settings", "/modules/client-modules", "/modules/check", "/modules/groups",
          "/modules/available", "/modules/catalog", "/modules/catalog-rich", "/modules/group-modules" };

    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        var method = ctx.HttpContext.Request.Method.ToUpperInvariant();
        var mutating = method is "POST" or "PUT" or "PATCH" or "DELETE";

        // Capture the arguments BEFORE the action runs (the model binder may mutate them after).
        string? payload = mutating ? SanitizePayload(ctx.ActionArguments) : null;

        var sw = Stopwatch.StartNew();
        var executed = await next();      // run the actual action
        sw.Stop();

        if (!mutating) return;
        var path = ctx.HttpContext.Request.Path.Value ?? "";
        if (SkipContains.Any(s => path.Contains(s, StringComparison.OrdinalIgnoreCase))) return;
        if (ReadOnlyPostSuffixes.Any(s => path.EndsWith(s, StringComparison.OrdinalIgnoreCase))) return;

        try
        {
            var req = ctx.HttpContext.Request;
            var cad = ctx.ActionDescriptor as ControllerActionDescriptor;
            var controller = cad?.ControllerName ?? "";
            var isBulk = path.StartsWith("/bulk/", StringComparison.OrdinalIgnoreCase);
            var module = FriendlyModule(controller, isBulk);
            var action = DeriveAction(method, path);
            var (clientCode, subResource, entityId) = ParseScope(ctx, path);
            var (subModule, entityType) = SubInfo(subResource);
            var client = await ResolveClientAsync(clientCode);

            // Body-based endpoints (Module Settings add/edit, Copy Modules, Tool Authority, Module Groups)
            // carry the client + the affected module in the REQUEST BODY, not the route — so the route
            // parse above finds nothing. Pull them out so the Client column + Summary are specific
            // (e.g. "IA00274 · AnkitTesting" + "Created Module \"Sales Order\" in Module Settings").
            string? entityName = null;
            if (client is null || entityType is null)
            {
                var (bodyCode, bodyCatalog, bodyEntity) = ParseBody(ctx);
                client ??= await ResolveClientAsync(bodyCode) ?? await ResolveClientByCatalogAsync(bodyCatalog);
                entityName = bodyEntity;
                if (!string.IsNullOrWhiteSpace(entityName) && entityType is null) entityType = "Module";
            }
            // Show the module name as the record when there's no numeric route id.
            if (string.IsNullOrWhiteSpace(entityId) && !string.IsNullOrWhiteSpace(entityName)) entityId = entityName;
            var status = (executed.Result as Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)?.StatusCode
                         ?? ctx.HttpContext.Response.StatusCode;
            var success = executed.Exception == null && status is >= 200 and < 400;

            int? userId = TryInt(req.Headers["UserID"]);
            var ua = req.Headers["User-Agent"].ToString();
            var (browser, os, device) = UserAgentParser.Parse(ua);

            var entry = new AuditEntry
            {
                CreatedAt = DateTime.UtcNow,
                UserId = userId,
                UserName = await ResolveNameAsync(userId),
                Action = action,
                Module = module,
                SubModule = subModule,
                Client = client,
                EntityType = entityType,
                EntityId = entityId,
                Summary = BuildSummary(action, module, subModule, entityType, entityId, entityName, success),
                HttpMethod = method,
                Path = path,
                StatusCode = status,
                Success = success,
                DurationMs = (int)sw.ElapsedMilliseconds,
                IpAddress = ClientIp(ctx),
                Browser = browser,
                Os = os,
                Device = device,
                DeviceId = Str(req.Headers["X-Device-Id"], 80),
                Fingerprint = Str(req.Headers["X-Device-Fp"], 40),
                UserAgent = ua,
                Payload = payload,
            };
            _ = _audit.InsertAsync(entry);   // fire-and-forget; InsertAsync swallows its own errors
        }
        catch { /* never break the request because of auditing */ }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> ModuleMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Clients"] = "Clients", ["Customers"] = "Customers", ["Subscription"] = "Customers",
        ["PointManagement"] = "Point Management", ["UserAdmin"] = "User Management", ["Users"] = "User Management",
        ["Modules"] = "Module Settings", ["ModuleGroup"] = "Module Groups", ["Keyline"] = "Keyline",
        ["Email"] = "Email", ["MasterTemplates"] = "Template Master", ["SopStatus"] = "SOP Modules",
        ["SopModules"] = "SOP Modules", ["ClientDocuments"] = "Client Documents", ["ProjectAssignment"] = "Project Assignment",
        ["Notification"] = "Notifications", ["Chat"] = "Messaging", ["Settings"] = "Settings", ["Auth"] = "Authentication",
    };

    private static string FriendlyModule(string controller, bool isBulk)
    {
        var name = ModuleMap.TryGetValue(controller, out var m) ? m : SplitPascal(controller);
        return isBulk ? $"Bulk Import · {name}" : name;
    }

    private static string SplitPascal(string s) =>
        string.IsNullOrEmpty(s) ? "General" : Regex.Replace(s.Replace("Controller", ""), "(?<=[a-z0-9])(?=[A-Z])", " ").Trim();

    private static string DeriveAction(string method, string path)
    {
        var p = path.ToLowerInvariant();
        if (p.Contains("/login") || p.Contains("authenticate") || p.Contains("signin")) return "Login";
        if (p.Contains("/logout") || p.Contains("signout")) return "Logout";
        if (p.Contains("export") || p.Contains("download") || p.Contains("backup")) return "Export";
        if (method == "DELETE" || p.Contains("delete") || p.Contains("remove") || p.Contains("softdelete")) return "Delete";
        if (p.Contains("seed")) return "Seed";
        if (method is "PUT" or "PATCH" || p.Contains("update") || p.Contains("/edit") || p.Contains("save") || p.Contains("marksent") || p.Contains("sync")) return "Update";
        return "Create";
    }

    private static readonly StringComparison OIC = StringComparison.OrdinalIgnoreCase;
    private static bool IsId(string s) => long.TryParse(s, out _) || Guid.TryParse(s, out _);

    /// <summary>Pull the client code, the sub-resource (tab) and the record id out of the route/path —
    /// e.g. /api/clients/IA00274/milestones/10 → (IA00274, "milestones", "10").</summary>
    private static (string? clientCode, string? subResource, string? entityId) ParseScope(ActionExecutingContext ctx, string path)
    {
        string? clientCode = null;
        foreach (var k in new[] { "code", "clientCode", "companyUserId", "companyUserID" })
            if (ctx.RouteData.Values.TryGetValue(k, out var v) && v is not null && !string.IsNullOrWhiteSpace(v.ToString()))
            { clientCode = v.ToString(); break; }

        var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries)
                       .Where(s => !s.Equals("api", OIC) && !s.Equals("bulk", OIC)).ToArray();

        string? subResource = null;
        if (!string.IsNullOrEmpty(clientCode))
        {
            var idx = Array.FindIndex(segs, s => s.Equals(clientCode, OIC));
            if (idx >= 0 && idx + 1 < segs.Length && !IsId(segs[idx + 1])) subResource = segs[idx + 1];
        }

        string? entityId = null;
        foreach (var k in new[] { "id", "pointId", "moduleId", "userId", "uid", "templateId" })
            if (ctx.RouteData.Values.TryGetValue(k, out var v) && v is not null) { entityId = v.ToString(); break; }
        entityId ??= segs.LastOrDefault(IsId);
        return (clientCode, subResource, entityId);
    }

    /// <summary>Friendly tab + singular entity for a client sub-resource.</summary>
    private static (string? tab, string? entity) SubInfo(string? sub) => (sub?.ToLowerInvariant()) switch
    {
        null => (null, null),
        "milestones" => ("Tracker · Milestones", "Milestone"),
        "training" or "trainings" or "trainingupdates" => ("Tracker · Training", "Training"),
        "changerequests" or "changerequest" => ("Tracker · Change Requests", "Change Request"),
        "communication" or "communicationlog" or "communications" => ("Communication Log", "Communication"),
        "onsite" or "onsitevisits" => ("Onsite Management", "Onsite Visit"),
        "support" or "supportlogs" => ("Support", "Support Log"),
        "documents" or "clientdocuments" => ("Client Documents", "Document"),
        "kickoff" => ("Kick-Off", "Kick-Off"),
        "signoff" => ("Sign-Off", "Sign-Off"),
        "modules" or "settings" => ("Module Settings", "Module"),
        "milestones-init" or "init-roadmap" => ("Tracker · Roadmap", "Roadmap"),
        _ => (SplitPascal(char.ToUpper(sub[0]) + sub[1..]), "Record"),
    };

    private static string BuildSummary(string action, string module, string? subModule, string? entityType, string? entityId, string? entityName, bool success)
    {
        var verb = action switch
        {
            "Create" => "Created", "Update" => "Updated", "Delete" => "Deleted",
            "Login" => "Logged in", "Logout" => "Logged out", "Export" => "Exported", "Seed" => "Seeded", _ => action
        };
        var what = entityType ?? module;
        var s = $"{verb} {what}";
        // A named record (e.g. a module) reads "…Module \"Sales Order\""; a numeric key reads "…#10".
        if (!string.IsNullOrWhiteSpace(entityName)) s += $" \"{entityName}\"";
        else if (!string.IsNullOrWhiteSpace(entityId)) s += $" #{entityId}";
        var where = subModule ?? module;
        if (!string.IsNullOrWhiteSpace(where) && where != what) s += $" in {where}";
        if (!success) s += " — failed";
        return s;
    }

    // ── body-scope extraction (endpoints that carry client + module in the request body) ────────────

    /// <summary>Reflectively pull a client code, a DB catalog (from a connection string) and the affected
    /// module name out of the bound action arguments — for endpoints (Module Settings, Copy Modules,
    /// Tool Authority, Module Groups) whose target isn't in the route.</summary>
    private static (string? code, string? catalog, string? entityName) ParseBody(ActionExecutingContext ctx)
    {
        string? code = null, catalog = null, entityName = null;
        foreach (var arg in ctx.ActionArguments.Values)
        {
            if (arg is null || arg is string || arg.GetType().IsPrimitive) continue;
            code ??= GetStr(arg, "CompanyUserID") ?? GetStr(arg, "TargetCompanyUserID") ?? GetStr(arg, "CompanyUniqueCode");
            if (catalog is null)
            {
                var cs = GetStr(arg, "ConnectionString") ?? GetStr(arg, "SourceConnectionString");
                if (cs is not null) catalog = ParseCatalog(cs);
            }
            if (entityName is null)
            {
                var modObj = arg.GetType().GetProperty("Module")?.GetValue(arg);   // ClientModuleRequest.Module.ModuleName
                if (modObj is not null) entityName = GetStr(modObj, "ModuleDisplayName") ?? GetStr(modObj, "ModuleName");
                entityName ??= GetStr(arg, "ModuleGroupName") ?? GetStr(arg, "ModuleName");
            }
        }
        return (code, catalog, entityName);
    }

    /// <summary>Read a non-empty string property by name, else null (used on bound DTOs).</summary>
    private static string? GetStr(object obj, string prop)
    {
        var p = obj.GetType().GetProperty(prop);
        if (p is null || p.PropertyType != typeof(string)) return null;
        var v = p.GetValue(obj) as string;
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    /// <summary>Extract the database name from a connection string's "Initial Catalog=" / "Database=".</summary>
    private static string? ParseCatalog(string cs)
    {
        var m = Regex.Match(cs, @"(?:Initial\s+Catalog|Database)\s*=\s*([^;]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static readonly ConcurrentDictionary<string, string> _catalogCache = new();
    /// <summary>Resolve a client DB name (Initial Catalog) → "IA00274 · Company Name" by matching the
    /// control table's stored Conn_String (cached, best-effort; falls back to the raw DB name).</summary>
    private async Task<string?> ResolveClientByCatalogAsync(string? catalog)
    {
        if (string.IsNullOrWhiteSpace(catalog)) return null;
        if (_catalogCache.TryGetValue(catalog, out var cached)) return cached;
        string? label = null;
        try
        {
            await using var ctrl = await _db.OpenControlAsync();
            var row = await ctrl.QueryFirstOrDefaultAsync(
                @"SELECT TOP 1 CompanyUniqueCode AS Code, CompanyName AS Name
                  FROM Indus_Company_Authentication_For_Web_Modules
                  WHERE Conn_String LIKE '%Initial Catalog=' + @cat + ';%'
                     OR Conn_String LIKE '%Initial Catalog=' + @cat
                     OR Conn_String LIKE '%Database=' + @cat + ';%'
                     OR Conn_String LIKE '%Database=' + @cat",
                new { cat = catalog });
            if (row is not null)
            {
                string? name = row.Name, cc = row.Code;
                if (!string.IsNullOrWhiteSpace(name))
                    label = string.IsNullOrWhiteSpace(cc) ? name : $"{cc} · {name}";
            }
        }
        catch { }
        label ??= catalog;   // never leave the Client column blank — at least show the DB name
        _catalogCache[catalog] = label!;
        return label;
    }

    private static readonly ConcurrentDictionary<string, string> _clientCache = new();
    /// <summary>Resolve a client code (CompanyUniqueCode) → "IA00274 · Company Name" (cached, best-effort).</summary>
    private async Task<string?> ResolveClientAsync(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        if (_clientCache.TryGetValue(code, out var cached)) return cached;
        var label = code;
        try
        {
            await using var ctrl = await _db.OpenControlAsync();
            var name = await ctrl.ExecuteScalarAsync<string?>(
                "SELECT TOP 1 CompanyName FROM Indus_Company_Authentication_For_Web_Modules WHERE CompanyUniqueCode = @c OR CompanyUserID = @c",
                new { c = code });
            if (!string.IsNullOrWhiteSpace(name)) label = $"{code} · {name}";
        }
        catch { }
        _clientCache[code] = label!;
        return label;
    }

    private static string? ClientIp(ActionExecutingContext ctx)
    {
        var fwd = ctx.HttpContext.Request.Headers["X-Forwarded-For"].ToString();
        var raw = !string.IsNullOrWhiteSpace(fwd) ? fwd.Split(',')[0].Trim()
                                                  : ctx.HttpContext.Connection.RemoteIpAddress?.ToString();
        return UserAgentParser.NormalizeIp(raw);
    }

    private static int? TryInt(string? s) => int.TryParse(s, out var v) && v > 0 ? v : null;

    private static string? Str(Microsoft.Extensions.Primitives.StringValues v, int max)
    {
        var s = v.ToString();
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length > max ? s[..max] : s;
    }

    private async Task<string?> ResolveNameAsync(int? userId)
    {
        if (userId is null or <= 0) return null;
        if (_nameCache.TryGetValue(userId.Value, out var cached)) return cached;
        try
        {
            await using var c = await _db.OpenAsync();
            var name = await c.ExecuteScalarAsync<string?>(
                "SELECT TOP 1 FullName FROM app.Users WHERE UserID = @id", new { id = userId.Value });
            if (!string.IsNullOrWhiteSpace(name)) { _nameCache[userId.Value] = name!; return name; }
        }
        catch { }
        return null;
    }

    private static readonly Regex Sensitive = new(
        @"(""(?:password|pwd|pass|token|secret|conn_?string|connectionstring|apikey)""\s*:\s*)""[^""]*""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string? SanitizePayload(IDictionary<string, object?> args)
    {
        if (args.Count == 0) return null;
        try
        {
            var json = JsonSerializer.Serialize(args, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
            json = Sensitive.Replace(json, "$1\"***\"");
            return json.Length > 4000 ? json[..4000] + "…" : json;
        }
        catch { return null; }
    }
}
