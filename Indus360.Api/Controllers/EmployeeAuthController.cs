using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace Indus360.Api.Controllers;

/// <summary>
/// Login against the consolidated app.Users table (Employee HR data merged in — Users
/// is the single source of truth). Username = Email, password compared against
/// Users.PasswordHash (plain-text — recommend BCrypt later). Only active users may
/// sign in. Returns the app identity (appUser) so the frontend opens the full app +
/// sidebar, plus the HR profile fields (kept under "employee" for the portal page).
/// </summary>
[ApiController]
[Route("api/employee-auth")]
public sealed class EmployeeAuthController : ControllerBase
{
    private readonly Db _db;
    public EmployeeAuthController(Db db) => _db = db;

    public sealed record LoginRequest(string? Email, string? Password);

    private sealed class UserRow
    {
        public long UserId { get; set; }
        public int? EmployeeID { get; set; }
        public string? EmployeeCode { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Mobile { get; set; }
        public string? PersonalEmail { get; set; }
        public string? WorkLocation { get; set; }
        public string? EmployeeType { get; set; }
        public string? Status { get; set; }
        public string? PhotoPath { get; set; }
        public DateTime? DateOfJoining { get; set; }
        public int? DesignationID { get; set; }
        public int? DepartmentID { get; set; }
        public bool? IsActive { get; set; }
        public string? PasswordHash { get; set; }
        public string? Role { get; set; }
        public int CompanyId { get; set; }
        public long ProductionUnitId { get; set; }
        public string? FYear { get; set; }
        public string? CompanyUsername { get; set; }
        public string? CompanyPassword { get; set; }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var email = (req?.Email ?? "").Trim();
        var password = (req?.Password ?? "").Trim();
        if (email.Length == 0 || password.Length == 0)
            return BadRequest(new { success = false, message = "Email and password are required." });

        try
        {
            using var c = await _db.OpenAsync(); // consolidated DB, app schema
            var u = await c.QueryFirstOrDefaultAsync<UserRow>(@"
                SELECT TOP 1 UserId, EmployeeID, EmployeeCode, FullName, Email, Mobile, PersonalEmail,
                       WorkLocation, EmployeeType, Status, PhotoPath, DateOfJoining, DesignationID, DepartmentID,
                       IsActive, PasswordHash, Role, CompanyId, ProductionUnitId, FYear, CompanyUsername, CompanyPassword
                FROM app.Users
                WHERE LTRIM(RTRIM(Email)) = @email AND ISNULL(IsDeletedTransaction,0) = 0
                ORDER BY CASE WHEN ISNULL(IsActive,0)=1 THEN 0 ELSE 1 END, UserId",
                new { email });

            if (u is null)
                return Unauthorized(new { success = false, message = "Invalid email or password." });
            if (u.IsActive != true)
                return Unauthorized(new { success = false, message = "This account is inactive. Please contact your administrator." });
            if (!string.Equals((u.PasswordHash ?? "").Trim(), password, StringComparison.Ordinal))
                return Unauthorized(new { success = false, message = "Invalid email or password." });

            var appUser = new SessionUser
            {
                UserID = u.UserId, FullName = u.FullName ?? "", Email = u.Email ?? "", Role = u.Role ?? "User",
                CompanyID = u.CompanyId, ProductionUnitID = u.ProductionUnitId, FYear = u.FYear ?? "",
                CompanyUsername = u.CompanyUsername ?? "", CompanyPassword = u.CompanyPassword ?? "",
            };

            return Ok(new
            {
                success = true,
                hasAppAccess = true,
                appUser,
                employee = new
                {
                    u.EmployeeID, u.EmployeeCode, u.FullName, u.Email, PhoneNumber = u.Mobile,
                    u.PersonalEmail, u.WorkLocation, u.EmployeeType, u.Status, u.PhotoPath,
                    u.DateOfJoining, u.DesignationID, u.DepartmentID,
                },
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Login failed: " + ex.Message });
        }
    }
}
