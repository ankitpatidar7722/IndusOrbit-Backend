using Dapper;
using Indus360.Api.Data;
using Microsoft.Extensions.Options;

namespace Indus360.Api.Services;

/// <summary>
/// Background worker that ports WhatsAppNotifier.RunDeadlineCheck +
/// AutoWhatsAppReport: every 60s it DMs assignees whose points are due within
/// ±20 min, and sends a morning/evening status report. No-ops unless
/// WhatsApp:Enabled is true (safe by default — won't message real users).
/// </summary>
public sealed class DeadlineReminderService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly WhatsAppOptions _opt;
    private readonly ILogger<DeadlineReminderService> _log;
    private DateOnly _lastMorning = DateOnly.MinValue;
    private DateOnly _lastEvening = DateOnly.MinValue;

    public DeadlineReminderService(IServiceProvider sp, IOptions<WhatsAppOptions> opt, ILogger<DeadlineReminderService> log)
    {
        _sp = sp;
        _opt = opt.Value;
        _log = log;
    }

    private sealed class DueRow { public int PointID { get; set; } public string? Title { get; set; } public string? WhatsAppNumber { get; set; } }
    private sealed class StatusCount { public string Status { get; set; } = ""; public int C { get; set; } }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { if (_opt.Enabled) await TickAsync(); }
            catch (Exception ex) { _log.LogError(ex, "DeadlineReminderService tick failed"); }
            try { await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken); } catch (TaskCanceledException) { break; }
        }
    }

    private async Task TickAsync()
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Db>();
        var sender = scope.ServiceProvider.GetRequiredService<WhatsAppSender>();
        await using var conn = await db.OpenTmsAsync();

        // 1) Deadline reminders (±20 min window, once per point)
        var due = await conn.QueryAsync<DueRow>(@"
            SELECT p.PointID, p.Title, u.WhatsAppNumber
            FROM dbo.Points p
            JOIN dbo.Users u ON u.UserID = p.AssignedToID
            WHERE p.Status IN ('Assigned','In Progress')
              AND ISNULL(p.IsDeadlineNotified,0) = 0
              AND u.WhatsAppNumber IS NOT NULL AND LTRIM(RTRIM(u.WhatsAppNumber)) <> ''
              AND p.ExpectedDate IS NOT NULL
              AND p.ExpectedDate BETWEEN DATEADD(MINUTE,-20,GETDATE()) AND DATEADD(MINUTE,20,GETDATE())");
        foreach (var d in due)
        {
            var ok = await sender.SendAsync(d.WhatsAppNumber!, $"Reminder: Point #{d.PointID} \"{d.Title}\" is due within ~20 minutes.");
            if (ok) await conn.ExecuteAsync("UPDATE dbo.Points SET IsDeadlineNotified = 1 WHERE PointID = @id", new { id = d.PointID });
        }

        // 2) Daily status report (morning ~9:00, evening ~20:00), once each per day
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var wantMorning = now.Hour == 9 && _lastMorning != today;
        var wantEvening = now.Hour == 20 && _lastEvening != today;
        if ((wantMorning || wantEvening) && !string.IsNullOrWhiteSpace(_opt.ReportMobile) && _opt.ReportMobile.Contains('+'))
        {
            var counts = await conn.QueryAsync<StatusCount>(
                "SELECT Status, COUNT(*) AS C FROM dbo.Points WHERE CAST(DateCreated AS date) = CAST(GETDATE() AS date) GROUP BY Status");
            var lines = string.Join("\n", counts.Select(c => $"{c.Status}: {c.C}"));
            var msg = $"Indus Task — {(wantMorning ? "Morning" : "Evening")} report ({today:dd-MMM}):\n{(string.IsNullOrEmpty(lines) ? "No new points today." : lines)}";
            if (await sender.SendAsync(_opt.ReportMobile, msg))
            {
                if (wantMorning) _lastMorning = today; else _lastEvening = today;
            }
        }
    }
}
