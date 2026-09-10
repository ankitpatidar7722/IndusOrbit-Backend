using Indus360.Api.Data;
using Indus360.Api.Repositories;
// ── BulkImport (folded in — see BulkImport/ folder; its endpoints serve under /bulk/api/...) ──
using Backend.DTOs;
using Backend.Services;
using Indus360.Api.BulkImportSupport;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Data.SqlClient;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// BulkImport bulk-Excel uploads can be large — allow up to 500 MB request bodies (Kestrel).
// (Under IIS, web.config's maxAllowedContentLength must also allow it.)
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 524_288_000);

// Resilience: a crash in a background worker (e.g. DeadlineReminderService hitting a
// transient DB error) must NOT take down the whole API host. Log-and-continue instead.
builder.Services.Configure<HostOptions>(o =>
    o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

builder.Services.AddControllers(o =>
    {
        // Route the folded-in BulkImport controllers under /bulk/... so they don't collide
        // with Indus360's own /api/... controllers (both ship Auth/Health/Keyline/MessageFormat).
        o.Conventions.Add(new BulkImportRoutePrefixConvention());
    })
    .AddJsonOptions(options =>
    {
        // Lenient INPUT parsing BulkImport relies on (numbers arriving as strings from Excel).
        // Input-only — does NOT change Indus360 response shape. Intentionally NOT setting
        // DefaultIgnoreCondition=WhenWritingNull (that would drop nulls from Indus360 responses).
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.NumberHandling =
            System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString |
            System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Data access (Dapper) — one connection factory, scoped repository
builder.Services.AddSingleton<Db>();
// In-memory cache for read-heavy shared-DB data (subscription list/stats, keyline catalog,
// dashboard KPIs) — cuts repeated remote-DB queries under concurrent load. See CacheService.
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<Indus360.Api.Services.CacheService>();
// Gemini (free API) — tracker row AI summaries. Stateless; key read from config/env at call time.
builder.Services.AddHttpClient();
builder.Services.AddSingleton<Indus360.Api.Services.GeminiService>();
builder.Services.AddScoped<ClientRepository>();
builder.Services.AddScoped<NavRepository>();
builder.Services.AddScoped<SubscriptionRepository>();
builder.Services.AddScoped<ClientExceedRepository>();
builder.Services.AddScoped<ProvisioningRepository>();
builder.Services.AddScoped<ModulesRepository>();
builder.Services.AddScoped<UserAdminRepository>();
builder.Services.AddScoped<ProjectAssignmentRepository>();
builder.Services.AddScoped<ClientTabPermissionRepository>();
builder.Services.AddScoped<MessagingRepository>();
builder.Services.AddScoped<AppNotificationRepository>();
builder.Services.AddScoped<PushSubscriptionRepository>();
builder.Services.AddSingleton<Indus360.Api.Services.WebPushSender>();
builder.Services.AddScoped<Indus360.Api.Services.NotificationPusher>();
builder.Services.AddSingleton<Indus360.Api.Services.PresenceTracker>();
builder.Services.AddSignalR();
// Point Management module (IndusTaskManagement DB)
builder.Services.AddScoped<TmsLookupRepository>();
builder.Services.AddScoped<PointRepository>();
builder.Services.AddScoped<TmsUserRepository>();
builder.Services.AddScoped<PointWorkflowRepository>();
builder.Services.AddScoped<TicketRepository>();
builder.Services.AddScoped<TmsReportRepository>();
builder.Services.AddScoped<TmsAdminRepository>();
builder.Services.AddScoped<NotificationRepository>();
builder.Services.AddScoped<TemplateStatusRepository>();
builder.Services.AddScoped<AttachmentRepository>();
// Client Kick-Off / Sign-Off finalized documents (save / view / download)
builder.Services.AddScoped<ClientDocumentRepository>();
builder.Services.AddScoped<SignoffDataRepository>();
builder.Services.AddScoped<KeylineRepository>();
// CRM client picker (reads IndusAppDB.dbo.Customers — the internal CRM app's data, same DB)
builder.Services.AddScoped<CrmRepository>();
// Email (compose/send/history) — migrated from the legacy Indas Estimo email feature
builder.Services.AddScoped<Indus360.Api.Repositories.EmailRepository>();
builder.Services.AddScoped<Indus360.Api.Repositories.EmailTemplateRepository>();
builder.Services.AddSingleton<Indus360.Api.Services.EmailSender>();
builder.Services.AddSingleton<Indus360.Api.Services.ImapReader>();
builder.Services.AddSingleton<Indus360.Api.Services.PdfRenderer>();

// WhatsApp integration + deadline-reminder background worker (disabled unless WhatsApp:Enabled=true)
builder.Services.Configure<Indus360.Api.Services.WhatsAppOptions>(builder.Configuration.GetSection("WhatsApp"));
builder.Services.AddHttpClient<Indus360.Api.Services.WhatsAppSender>();
builder.Services.AddHostedService<Indus360.Api.Services.DeadlineReminderService>();

// ─────────────────────────────────────────────────────────────────────────────
// BulkImport (folded in) — JWT auth, per-request tenant SqlConnection, and its services.
// Controllers live in BulkImport/Controllers (namespace Backend.Controllers) and are routed
// under /bulk/api/... by BulkImportRoutePrefixConvention. Kept fully separate from Indus360's
// own Db factory + repositories (different types, no overlap).
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();

// JWT bearer for BulkImport. Indus360's own controllers carry no [Authorize], so they stay open.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]
                    ?? "ThisIsSamplesecretKey12345678901234567890")),
        };
    });

// BulkImport's per-request tenant SqlConnection: session (JWT sessionId claim) → else IndusConnection.
builder.Services.AddScoped<SqlConnection>(sp =>
{
    var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
    var config = sp.GetRequiredService<IConfiguration>();
    var sessionStore = sp.GetRequiredService<ICompanySessionStore>();
    var httpContext = httpContextAccessor.HttpContext;
    if (httpContext?.User != null)
    {
        var sessionIdClaim = httpContext.User.FindFirst("sessionId")?.Value;
        if (!string.IsNullOrEmpty(sessionIdClaim) && Guid.TryParse(sessionIdClaim, out var sessionId)
            && sessionStore.TryGetSession(sessionId, out var session) && session != null)
        {
            var connBuilder = new SqlConnectionStringBuilder(session.ConnectionString) { TrustServerCertificate = true };
            return new SqlConnection(connBuilder.ConnectionString);
        }
    }
    var defaultConn = config.GetConnectionString("IndusConnection");
    return string.IsNullOrEmpty(defaultConn) ? new SqlConnection() : new SqlConnection(defaultConn);
});

builder.Services.AddSingleton<ICompanySessionStore, CompanySessionStore>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IModuleService, ModuleService>();
builder.Services.AddScoped<IExcelService, ExcelService>();
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<ILedgerService, LedgerService>();
builder.Services.AddScoped<IHSNService, HSNService>();
builder.Services.AddScoped<ISparePartService, SparePartService>();
builder.Services.AddScoped<IItemService, ItemService>();
builder.Services.AddScoped<IToolService, ToolService>();
builder.Services.AddScoped<IItemStockService, ItemStockService>();
builder.Services.AddScoped<IModuleAuthorityService, ModuleAuthorityService>();
builder.Services.AddScoped<ISparePartMasterStockService, SparePartMasterStockService>();
builder.Services.AddScoped<IToolStockService, ToolStockService>();
builder.Services.AddScoped<ICompanySubscriptionService, CompanySubscriptionService>();
builder.Services.AddScoped<IFeatureSubscriptionService, FeatureSubscriptionService>();
builder.Services.AddScoped<IPlanCatalogService, PlanCatalogService>();
builder.Services.AddScoped<IMessageFormatService, MessageFormatService>();
builder.Services.AddScoped<IActivityLogService, ActivityLogService>();
builder.Services.AddScoped<IDatabaseBackupRestoreService, DatabaseBackupRestoreService>();
builder.Services.AddScoped<IContentAuthorityService, ContentAuthorityService>();
builder.Services.AddScoped<IKeylineService, KeylineService>();
builder.Services.Configure<BackupRestoreConfig>(builder.Configuration.GetSection("BackupRestore"));

// EPPlus (BulkImport ExcelService) license context.
OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

// CORS for the Next.js frontend
var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
              ?? new[] { "http://localhost:3000" };
builder.Services.AddCors(o => o.AddPolicy("frontend", p => p
    .WithOrigins(origins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()
    // Cache the CORS preflight (OPTIONS) result in the browser for 2h (Chrome's max) so it
    // doesn't re-preflight before every credentialed cross-origin call — halves API round-trips.
    .SetPreflightMaxAge(TimeSpan.FromHours(2))));

var app = builder.Build();

// BulkImport's original idempotent startup migrations (IF-NOT-EXISTS DDL). Tables already exist
// in prod, so normally a no-op. Toggle off with config BulkImport:RunStartupMigrations=false.
if (builder.Configuration.GetValue("BulkImport:RunStartupMigrations", true))
    BulkImportStartup.RunStartupMigrations(app);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<Indus360.Api.Hubs.MessagingHub>("/messagingHub");
app.MapGet("/", () => Results.Ok(new { service = "Indus 360 API", status = "ok" }));

app.Run();
