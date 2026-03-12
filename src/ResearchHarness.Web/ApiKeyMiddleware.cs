using System.Security.Cryptography;
using System.Text;

namespace ResearchHarness.Web;

/// <summary>
/// Guards /internal/ and /admin routes with API key validation.
/// Accepts the key via X-Api-Key header (API clients) or admin_key cookie (browser).
/// If ApiKey is not configured (empty), the check is skipped — dev convenience only.
/// </summary>
public sealed class ApiKeyMiddleware
{
    private const string HeaderName = "X-Api-Key";
    private const string CookieName = "admin_key";
    private const string InternalPrefix = "/internal/";
    private const string AdminPrefix = "/admin";

    private readonly RequestDelegate _next;
    private readonly string _configuredKey;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuredKey = configuration["ApiKey"] ?? "";
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (string.IsNullOrEmpty(_configuredKey))
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "";

        var isProtected = path.StartsWith(InternalPrefix, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(AdminPrefix, StringComparison.OrdinalIgnoreCase);

        // Allow Blazor framework assets and static files through
        if (path.StartsWith("/_framework/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/css/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (isProtected)
        {
            // Try header first, then cookie
            var providedKey = context.Request.Headers.TryGetValue(HeaderName, out var headerKey)
                ? headerKey.ToString()
                : context.Request.Cookies[CookieName];

            if (string.IsNullOrEmpty(providedKey)
                || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(providedKey),
                    Encoding.UTF8.GetBytes(_configuredKey)))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized: invalid or missing API key.");
                return;
            }

            // Set cookie on successful auth so subsequent Blazor navigation works
            if (!context.Request.Cookies.ContainsKey(CookieName))
            {
                context.Response.Cookies.Append(CookieName, _configuredKey, new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Secure = context.Request.IsHttps,
                    MaxAge = TimeSpan.FromHours(8)
                });
            }
        }

        await _next(context);
    }
}
