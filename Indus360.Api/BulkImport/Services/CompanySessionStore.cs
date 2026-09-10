using System.Collections.Concurrent;

namespace Backend.Services;

public class CompanySessionStore : ICompanySessionStore
{
    private readonly ConcurrentDictionary<Guid, CompanySession> _sessions = new();

    public Guid CreateSession(string companyUserId, string connString, string companyName)
    {
        var session = new CompanySession
        {
            SessionId = Guid.NewGuid(),
            CompanyUserID = companyUserId,
            ConnectionString = connString,
            CompanyName = companyName,
            LoginStep = 1
        };

        _sessions.TryAdd(session.SessionId, session);
        return session.SessionId;
    }

    public bool TryGetSession(Guid sessionId, out CompanySession? session)
    {
        return _sessions.TryGetValue(sessionId, out session);
    }

    public void UpdateSession(Guid sessionId, CompanySession session)
    {
        _sessions.AddOrUpdate(sessionId, session, (key, oldVal) => session);
    }

    public void RemoveSession(Guid sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
    }
}
