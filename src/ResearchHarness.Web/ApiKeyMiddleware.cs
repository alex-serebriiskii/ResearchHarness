using System.Security.Cryptography;
using System.Text;

namespace ResearchHarness.Web;

/// <summary>
/// Phase 1 internal auth: validates X-Api-Key header for /internal/ routes.
/// If ApiKey is not configured (empty), the check is skipped — dev convenience only.
/// Phase 3+: replace with proper authentication (JWT, mutual TLS, etc.).
/// </summary>
public sealed class ApiKeyMiddleware
{
    private const string HeaderName = "X-Api-Key";
    private const string InternalPrefix = "/internal/";

    private readonly RequestDelegate _next;
    private readonly string _configuredKey;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuredKey = configuration["ApiKey"] ?? "";
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        if (path.StartsWith(InternalPrefix, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(_configuredKey))
        {
            if (!context.Request.Headers.TryGetValue(HeaderName, out var providedKey)
                || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(providedKey.ToString()),
                    Encoding.UTF8.GetBytes(_configuredKey)))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized: invalid or missing API key.");
                return;
            }
        }

        await _next(context);
    }
}
