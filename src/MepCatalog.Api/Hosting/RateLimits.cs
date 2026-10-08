using System.Threading.RateLimiting;

namespace MepCatalog.Api;

/// <summary>
/// Limits uploads (model audits, fixes, reports, datasheets) per client IP. These endpoints parse whole files
/// and are the expensive ones, so a public demo shouldn't let one visitor hog the server.
/// </summary>
public static class RateLimits
{
    public const string Uploads = "uploads";

    public static IServiceCollection AddUploadRateLimit(this IServiceCollection services, IConfiguration config)
    {
        var perMinute = config.GetValue("RateLimits:UploadsPerMinute", 30);
        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(Uploads, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) }));
        });
    }
}
