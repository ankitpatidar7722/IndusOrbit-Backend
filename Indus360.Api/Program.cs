using Indus360.Api.Data;
using Indus360.Api.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Resilience: a crash in a background worker (e.g. DeadlineReminderService hitting a
// transient DB error) must NOT take down the whole API host. Log-and-continue instead.
builder.Services.Configure<HostOptions>(o =>
    o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

builder.Services.AddControllers();
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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("frontend");
app.MapControllers();
app.MapHub<Indus360.Api.Hubs.MessagingHub>("/messagingHub");
app.MapGet("/", () => Results.Ok(new { service = "Indus 360 API", status = "ok" }));

app.Run();
