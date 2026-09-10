using Backend.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Backend.Attributes
{
    /// <summary>
    /// Custom authorization attribute that validates the X-Backup-Restore-ApiKey header
    /// Used for server-to-server authentication
    /// </summary>
    public class ValidateBackupApiKeyAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            // Get configuration
            var config = context.HttpContext.RequestServices
                .GetRequiredService<IOptions<BackupRestoreConfig>>().Value;

            // Check if API key header is present
            if (!context.HttpContext.Request.Headers.TryGetValue("X-Backup-Restore-ApiKey", out var apiKeyHeader))
            {
                // If no API key, check if user is authenticated (JWT)
                // This allows both API key and JWT authentication
                if (context.HttpContext.User?.Identity?.IsAuthenticated == true)
                {
                    // User is authenticated via JWT, allow the request
                    return;
                }

                context.Result = new UnauthorizedObjectResult(new
                {
                    error = "API key is required. Provide X-Backup-Restore-ApiKey header for server-to-server authentication."
                });
                return;
            }

            var apiKey = apiKeyHeader.ToString();

            // Validate API key
            if (string.IsNullOrWhiteSpace(config.ApiKey))
            {
                context.Result = new ObjectResult(new
                {
                    error = "Server configuration error: API key not configured in appsettings.json"
                })
                {
                    StatusCode = 500
                };
                return;
            }

            if (apiKey != config.ApiKey)
            {
                context.Result = new UnauthorizedObjectResult(new
                {
                    error = "Invalid API key. Authentication failed."
                });
                return;
            }

            // API key is valid, allow the request
        }
    }
}
