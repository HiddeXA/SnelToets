using System.Security.Cryptography;
using System.Text;

namespace SnelToets.Web.Mcp;

/// <summary>
/// Protects <c>/mcp</c> and all mutating <c>/api</c> requests with the key from configuration "Mcp:ApiKey".
/// Accepted headers: <c>Authorization: Bearer &lt;key&gt;</c> or <c>X-Api-Key: &lt;key&gt;</c>.
/// When no key is configured everything is allowed (development mode) and a warning is logged at startup.
/// </summary>
public sealed class ApiKeyGuard
{
    public const string ConfigKey = "Mcp:ApiKey";
    public const string HeaderName = "X-Api-Key";

    private readonly byte[]? _key;

    public ApiKeyGuard(IConfiguration configuration)
    {
        var key = configuration[ConfigKey]?.Trim();
        _key = string.IsNullOrEmpty(key) ? null : Encoding.UTF8.GetBytes(key);
    }

    /// <summary>True when an API key is configured (the key itself is never exposed).</summary>
    public bool IsEnabled => _key is not null;

    /// <summary>Whether this request must carry the API key.</summary>
    public static bool RequiresKey(HttpRequest request)
    {
        var path = request.Path;
        if (path.StartsWithSegments("/mcp")) return true;
        if (path.StartsWithSegments("/api"))
            return !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method));
        return false;
    }

    public bool IsAuthorized(HttpRequest request)
    {
        if (_key is null) return true;

        string? presented = null;
        var auth = request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            presented = auth["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(presented))
            presented = request.Headers[HeaderName].ToString().Trim();
        if (string.IsNullOrEmpty(presented)) return false;

        // Constant-time comparison (length differences are handled by FixedTimeEquals returning false).
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), _key);
    }
}

/// <summary>Middleware that enforces <see cref="ApiKeyGuard"/>; inserted at the start of the pipeline via an IStartupFilter.</summary>
internal sealed class ApiKeyMiddleware(RequestDelegate next, ApiKeyGuard guard)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (ApiKeyGuard.RequiresKey(context.Request) && !guard.IsAuthorized(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer realm=\"SnelToets\"";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
                title = "Niet geautoriseerd",
                status = 401,
                detail = "Geef de API-sleutel mee via 'Authorization: Bearer <sleutel>' of de header 'X-Api-Key'."
            }, options: null, contentType: "application/problem+json");
            return;
        }
        await next(context);
    }
}

internal sealed class ApiKeyStartupFilter(ApiKeyGuard guard, ILogger<ApiKeyGuard> logger) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        if (!guard.IsEnabled)
            logger.LogWarning(
                "Geen API-sleutel ingesteld (Mcp:ApiKey / Mcp__ApiKey): /mcp en de schrijvende /api-endpoints zijn voor iedereen open. " +
                "Stel een sleutel in voordat je SnelToets publiek bereikbaar maakt.");
        app.UseMiddleware<ApiKeyMiddleware>();
        next(app);
    };
}
