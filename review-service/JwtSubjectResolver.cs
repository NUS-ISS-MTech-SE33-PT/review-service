public static class JwtSubjectResolver
{
    public static string? ResolveUserId(HttpContext context)
    {
        var principal = context.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return principal.FindFirst("sub")?.Value;
    }
}
