using Microsoft.Extensions.Hosting;

namespace Indus360.Api.Services;

/// <summary>
/// Resolves the ROOT folder for user-uploaded files (profile photos, PM / chat attachments,
/// master templates). These must live OUTSIDE the deployed app folder, otherwise every redeploy
/// (which replaces the app folder) wipes them while the DB still points at the now-missing files.
///
/// Resolution order:
///   1. Env var <c>INDUS360_UPLOADS_ROOT</c> — explicit persistent path; survives redeploys and
///      overrides everything. Set this on the server for full control (e.g. D:\Indus360Uploads).
///   2. Development → the app content root (unchanged local behaviour, existing dev files keep working).
///   3. Production (default) → a SIBLING of the app folder (<c>&lt;contentRoot&gt;\..\Indus360-uploads</c>),
///      which a redeploy that swaps the app folder's contents does not touch. Zero server config needed.
/// </summary>
public static class StoragePaths
{
    public static string UploadsRoot(this IWebHostEnvironment env)
    {
        var configured = Environment.GetEnvironmentVariable("INDUS360_UPLOADS_ROOT");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        if (env.IsDevelopment()) return env.ContentRootPath;
        return Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "Indus360-uploads"));
    }

    /// <summary>Persistent sub-folder for a given upload kind (creates it if missing).</summary>
    public static string UploadsDir(this IWebHostEnvironment env, string subFolder)
    {
        var dir = Path.Combine(env.UploadsRoot(), subFolder);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
