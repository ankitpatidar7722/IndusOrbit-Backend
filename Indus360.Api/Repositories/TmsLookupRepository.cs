using Dapper;
using Indus360.Api.Data;
using Indus360.Api.Models;

namespace Indus360.Api.Repositories;

/// <summary>
/// Lookup lists for the Point Management module (users, customers, products,
/// categories) — reads the IndusTaskManagement database. Ports the small
/// GetUsers/GetCustomers/GetProducts/GetCategories helpers from DataAccess.vb.
/// </summary>
public sealed class TmsLookupRepository
{
    private readonly Db _db;
    public TmsLookupRepository(Db db) => _db = db;

    public async Task<IEnumerable<PmUser>> GetUsersAsync(string? role = null)
    {
        await using var db = await _db.OpenTmsAsync();
        // Exclude users disabled in dbo.Users OR in the app login (app.Users, matched by email) —
        // inactive users must never appear in an assignee picker. See inactive-user convention.
        const string sql = @"
            SELECT u.UserID, u.FullName, u.Email, u.Role, u.IsActive, u.WhatsAppNumber
            FROM dbo.Users u
            WHERE u.IsActive = 1
              AND (@role IS NULL OR u.Role = @role)
              AND NOT EXISTS (
                  SELECT 1 FROM app.Users a
                  WHERE NULLIF(LTRIM(RTRIM(a.Email)),'') = LTRIM(RTRIM(u.Email))
                    AND a.IsActive = 0 AND ISNULL(a.IsDeletedTransaction,0) = 0)
            ORDER BY u.FullName";
        return await db.QueryAsync<PmUser>(sql, new { role });
    }

    public async Task<IEnumerable<PmCustomer>> GetCustomersAsync()
    {
        await using var db = await _db.OpenTmsAsync();
        const string sql = @"
            SELECT CustomerID, CustomerName, CompanyName, ContactPerson, ContactEmail, ContactPhone, IsActive, DateCreated
            FROM dbo.Customers
            WHERE IsActive = 1
            ORDER BY CompanyName, CustomerName";
        return await db.QueryAsync<PmCustomer>(sql);
    }

    public async Task<IEnumerable<PmProduct>> GetProductsAsync()
    {
        await using var db = await _db.OpenTmsAsync();
        const string sql = @"
            SELECT ProductID, ProductName, ProductVersion, IsActive
            FROM dbo.Products
            WHERE IsActive = 1
            ORDER BY ProductName";
        return await db.QueryAsync<PmProduct>(sql);
    }

    public async Task<IEnumerable<PmCategory>> GetCategoriesAsync()
    {
        await using var db = await _db.OpenTmsAsync();
        const string sql = @"
            SELECT CategoryID, CategoryName, IsActive
            FROM dbo.Categories
            WHERE IsActive = 1
            ORDER BY CategoryName";
        return await db.QueryAsync<PmCategory>(sql);
    }
}
