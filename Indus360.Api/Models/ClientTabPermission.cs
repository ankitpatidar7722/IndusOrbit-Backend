namespace Indus360.Api.Models;

/// <summary>The current user's effective permission for one client-detail tab.</summary>
public sealed class ClientTabPermissionDto
{
    public string Key { get; set; } = "";   // company | authority | kickoff | tracker | templates | signoff | onsite
    public bool CanView { get; set; }
    public bool CanEdit { get; set; }
}
