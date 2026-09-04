using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace Indus360.Api.Services;

/// <summary>
/// Small in-memory cache for read-heavy, shared-across-users, rarely-changing data — the client
/// subscription list + header stats (read from the REMOTE control DB), the keyline module catalog,
/// and the dashboard KPIs. Registered as a singleton.
///
/// Includes cache-stampede protection: on a miss, only ONE caller runs the factory while the rest
/// wait on a per-key lock — so a 100-user login/refresh burst produces ONE database query, not 100.
/// Callers Remove(...) the relevant keys right after a write so in-app changes appear immediately
/// (the short TTL is only a safety net for changes made outside Indus 360).
/// </summary>
public sealed class CacheService
{
    private readonly IMemoryCache _cache;
    // One lock per cache key. The key set is small + fixed, so this dictionary never grows unbounded.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public CacheService(IMemoryCache cache) => _cache = cache;

    /// <summary>
    /// Return the cached value for <paramref name="key"/>, or run <paramref name="factory"/> exactly
    /// once (under a per-key lock) and cache the result for <paramref name="ttl"/>.
    /// </summary>
    public async Task<T> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory)
    {
        if (_cache.TryGetValue(key, out T? hit)) return hit!;

        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Re-check inside the lock: another caller may have populated it while we waited.
            if (_cache.TryGetValue(key, out T? hit2)) return hit2!;
            var value = await factory().ConfigureAwait(false);
            _cache.Set(key, value, ttl);
            return value;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Drop cached entries — call right after a write so the next read reloads fresh data.</summary>
    public void Remove(params string[] keys)
    {
        foreach (var k in keys) _cache.Remove(k);
    }
}
