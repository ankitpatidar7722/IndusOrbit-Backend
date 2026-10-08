namespace Indus360.Api.Services;

/// <summary>
/// Password hashing for app.Users.PasswordHash. New/changed passwords are stored as BCrypt hashes.
/// Legacy rows still hold plain text — <see cref="Verify"/> accepts those so nobody is locked out, and
/// each login lazily upgrades the stored value to a hash (see the login repositories). BCrypt carries
/// its own per-password salt inside the hash string, so no separate salt column is needed.
/// </summary>
public static class PasswordHasher
{
    private const int WorkFactor = 11;   // ~2^11 rounds — strong yet fast enough for interactive login

    /// <summary>Hash a plain password (BCrypt, salted).</summary>
    public static string Hash(string plain) => BCrypt.Net.BCrypt.HashPassword(plain ?? "", WorkFactor);

    /// <summary>A BCrypt hash string starts with "$2". Anything else is a legacy plain-text value.</summary>
    public static bool IsHashed(string? stored) =>
        !string.IsNullOrEmpty(stored) && stored.Length > 3 && stored[0] == '$' && stored[1] == '2';

    /// <summary>True if the password matches the stored value — BCrypt-verifies a hash, or compares a
    /// legacy plain-text value directly (so pre-migration accounts keep working).</summary>
    public static bool Verify(string? plain, string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;
        plain ??= "";
        if (IsHashed(stored))
        {
            try { return BCrypt.Net.BCrypt.Verify(plain, stored); }
            catch { return false; }
        }
        return string.Equals(stored, plain, System.StringComparison.Ordinal);   // legacy plain text
    }
}
