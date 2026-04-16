using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

public class ConfigureJwtBearerOptions : IConfigureNamedOptions<JwtBearerOptions>
{
    private readonly IOptions<JwtValidationOptions> _jwtValidationOptions;

    public ConfigureJwtBearerOptions(IOptions<JwtValidationOptions> jwtValidationOptions)
    {
        _jwtValidationOptions = jwtValidationOptions;
    }

    public void Configure(string? name, JwtBearerOptions options)
    {
        if (!string.Equals(name, JwtBearerDefaults.AuthenticationScheme, StringComparison.Ordinal))
        {
            return;
        }

        Configure(options);
    }

    public void Configure(JwtBearerOptions options)
    {
        var jwtOptions = _jwtValidationOptions.Value;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateLifetime = true,
            ValidateAudience = false,
            NameClaimType = "sub",
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        if (!string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
        {
            options.RequireHttpsMetadata = false;
            options.TokenValidationParameters.ValidateIssuerSigningKey = true;
            options.TokenValidationParameters.IssuerSigningKey =
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));
        }
        else
        {
            options.Authority = jwtOptions.Issuer;
        }

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var principal = context.Principal;
                var resolvedOptions = context.HttpContext.RequestServices
                    .GetRequiredService<IOptions<JwtValidationOptions>>()
                    .Value;

                var subject = principal?.FindFirst("sub")?.Value;
                if (string.IsNullOrWhiteSpace(subject))
                {
                    context.Fail("Missing sub claim.");
                    return Task.CompletedTask;
                }

                var tokenUse = principal?.FindFirst("token_use")?.Value;
                if (!string.Equals(tokenUse, "access", StringComparison.Ordinal))
                {
                    context.Fail("Only Cognito access tokens are accepted.");
                    return Task.CompletedTask;
                }

                var clientId = principal?.FindFirst("client_id")?.Value
                    ?? principal?.FindFirst("aud")?.Value;
                if (string.IsNullOrWhiteSpace(clientId) ||
                    !resolvedOptions.AllowedClientIds.Contains(clientId, StringComparer.Ordinal))
                {
                    context.Fail("Token client_id is not allowed.");
                }

                return Task.CompletedTask;
            }
        };
    }
}
