using System.Collections.Concurrent;

namespace Indus360.Api.Services;

/// <summary>
/// Tracks which users are currently connected to the messaging hub, per company
/// (a user may have several tabs/connections). Singleton, thread-safe.
/// </summary>
public sealed class PresenceTracker
{
    // companyId → (userId → live connection count)
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<long, int>> _online = new();

    /// <summary>Register a connection. Returns true if the user just came online (first connection).</summary>
    public bool Connect(int companyId, long userId)
    {
        var users = _online.GetOrAdd(companyId, _ => new ConcurrentDictionary<long, int>());
        var wasOffline = !users.ContainsKey(userId);
        users.AddOrUpdate(userId, 1, (_, n) => n + 1);
        return wasOffline;
    }

    /// <summary>Drop a connection. Returns true if the user just went offline (last connection).</summary>
    public bool Disconnect(int companyId, long userId)
    {
        if (!_online.TryGetValue(companyId, out var users)) return false;
        var newCount = users.AddOrUpdate(userId, 0, (_, n) => n - 1);
        if (newCount <= 0)
        {
            users.TryRemove(userId, out _);
            return true;
        }
        return false;
    }

    /// <summary>The userIds (as strings) currently online for a company.</summary>
    public List<string> OnlineUsers(int companyId)
        => _online.TryGetValue(companyId, out var users)
            ? users.Keys.Select(k => k.ToString()).ToList()
            : new List<string>();
}
