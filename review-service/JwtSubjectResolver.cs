using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public static class JwtSubjectResolver
{
    public static string? ResolveUserId(HttpContext context)
    {
        var headerUser = context.Request.Headers["x-user-sub"].FirstOrDefault();
        if (!string.IsNullOrEmpty(headerUser))
        {
            return headerUser;
        }

        var authorization = context.Request.Headers["Authorization"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return null;
        }

        var parts = authorization.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !parts[0].Equals("Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = parts[1].Trim();
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var segments = token.Split('.');
        if (segments.Length < 2)
        {
            return null;
        }

        try
        {
            var payload = NormalizeBase64(segments[1]);
            var bytes = Convert.FromBase64String(payload);
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.TryGetProperty("sub", out var subElement) &&
                subElement.ValueKind == JsonValueKind.String)
            {
                return subElement.GetString();
            }
        }
        catch (Exception ex)
        {
            var logger = context.RequestServices
                .GetService<ILoggerFactory>()
                ?.CreateLogger(typeof(JwtSubjectResolver));
            logger?.LogWarning(ex, "Failed to resolve user subject from Authorization header.");
        }

        return null;
    }

    private static string NormalizeBase64(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        return (normalized.Length % 4) switch
        {
            2 => normalized + "==",
            3 => normalized + "=",
            1 => normalized + "===",
            _ => normalized
        };
    }
}
