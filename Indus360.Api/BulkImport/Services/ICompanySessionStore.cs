using System;

namespace Backend.Services;

public class CompanySession
{
    public Guid SessionId { get; set; } = Guid.NewGuid();
    public string ConnectionString { get; set; } = string.Empty;
    public string CompanyUserID { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public int UserID { get; set; }
    public int CompanyID { get; set; }
    public int BranchID { get; set; }
    public string FYear { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public int LoginStep { get; set; }
}

public interface ICompanySessionStore
{
    Guid CreateSession(string companyUserId, string connString, string companyName);
    bool TryGetSession(Guid sessionId, out CompanySession? session);
    void UpdateSession(Guid sessionId, CompanySession session);
    void RemoveSession(Guid sessionId);
}
