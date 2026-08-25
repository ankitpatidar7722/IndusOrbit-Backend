using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

public static class ControllerExtensions
{
    /// <summary>
    /// The acting user's id, read from the "UserID" request header (the frontend sends it from
    /// the logged-in session on every mutating call). Returns null if absent/invalid — audit
    /// columns then stay NULL rather than blocking the operation.
    /// </summary>
    public static int? CurrentUserId(this ControllerBase c)
    {
        if (c.Request.Headers.TryGetValue("UserID", out var h) && int.TryParse(h.ToString(), out var id) && id > 0)
            return id;
        return null;
    }

    /// <summary>The acting user's company id, from the "CompanyID" request header (defaults to 1).</summary>
    public static int CurrentCompanyId(this ControllerBase c)
    {
        if (c.Request.Headers.TryGetValue("CompanyID", out var h) && int.TryParse(h.ToString(), out var id) && id > 0)
            return id;
        return 1;
    }
}
