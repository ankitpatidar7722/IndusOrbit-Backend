using System.Collections.Concurrent;
using Indus360.Api.Models;

namespace Indus360.Api.Repositories;

/// <summary>Live progress of a background provisioning (setup-database) job — polled by the wizard UI.</summary>
public sealed class ProvisionProgress
{
    /// <summary>queued | backup | transfer | restore | finalize | done | error</summary>
    public string Stage { get; set; } = "queued";
    public string Message { get; set; } = "Starting…";
    public int Percent { get; set; }
    public bool Done { get; set; }
    public bool Success { get; set; }
    public SetupDatabaseResponse? Result { get; set; }
    public DateTime Touched { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// In-memory registry of running/finished provisioning jobs (singleton). setup-database runs in the
/// background (so the HTTP call returns immediately and never times out) and reports progress here;
/// the wizard polls it. Finished/stale jobs are purged after an hour so the map can't grow unbounded.
/// </summary>
public sealed class ProvisioningJobStore
{
    private readonly ConcurrentDictionary<string, ProvisionProgress> _jobs = new();

    public string New()
    {
        Purge();
        var id = Guid.NewGuid().ToString("N");
        _jobs[id] = new ProvisionProgress();
        return id;
    }

    public ProvisionProgress? Get(string id) => _jobs.TryGetValue(id, out var p) ? p : null;

    public void Update(string id, Action<ProvisionProgress> mutate)
    {
        if (_jobs.TryGetValue(id, out var p)) { mutate(p); p.Touched = DateTime.UtcNow; }
    }

    private void Purge()
    {
        var cutoff = DateTime.UtcNow.AddHours(-1);
        foreach (var kv in _jobs)
            if (kv.Value.Touched < cutoff) _jobs.TryRemove(kv.Key, out _);
    }
}
