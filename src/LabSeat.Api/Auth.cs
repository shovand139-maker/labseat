using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LabSeat.Api;

public static class Policies
{
    public const string Member = "member";
    public const string Learner = "learner";
    public const string Instructor = "instructor";
}

/// <summary>The signed-in caller, read from validated token claims.</summary>
public sealed record CurrentUser(Guid UserId, Guid TenantId, string Role)
{
    public bool IsInstructor => Role == "instructor";

    public static CurrentUser From(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue("sub");
        var tid = principal.FindFirstValue("tid");
        if (!Guid.TryParse(sub, out var userId) || !Guid.TryParse(tid, out var tenantId))
        {
            throw new UnauthorizedAccessException("The token is missing the sub or tid claim.");
        }
        return new CurrentUser(userId, tenantId, principal.FindFirstValue("role") ?? string.Empty);
    }
}

public static class AuthSetup
{
    /// <summary>
    /// Production: set Auth:Authority and Auth:Audience (Microsoft Entra ID) and map app roles to the
    /// "role" claim and the organization to "tid". Development and tests: HS256 tokens from DevTokens.
    /// Options are bound lazily so test hosts can override configuration.
    /// </summary>
    public static IServiceCollection AddLabSeatAuth(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IConfiguration>((options, config) =>
            {
                options.MapInboundClaims = false;
                var authority = config["Auth:Authority"];
                if (!string.IsNullOrWhiteSpace(authority))
                {
                    options.Authority = authority;
                    options.Audience = config["Auth:Audience"];
                    options.TokenValidationParameters.NameClaimType = "name";
                    options.TokenValidationParameters.RoleClaimType = "role";
                    return;
                }

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = DevTokens.Issuer,
                    ValidAudience = DevTokens.Audience,
                    IssuerSigningKey = DevTokens.SigningKey(config),
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "name",
                    RoleClaimType = "role",
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Member, p => p.RequireAuthenticatedUser().RequireClaim("tid").RequireClaim("sub"))
            .AddPolicy(Policies.Learner, p => p.RequireAuthenticatedUser().RequireClaim("tid").RequireClaim("role", "learner"))
            .AddPolicy(Policies.Instructor, p => p.RequireAuthenticatedUser().RequireClaim("tid").RequireClaim("role", "instructor"));

        return services;
    }
}

/// <summary>Issues short-lived HS256 tokens for local development and tests only.</summary>
public static class DevTokens
{
    public const string Issuer = "labseat-dev";
    public const string Audience = "labseat-api";

    public static SymmetricSecurityKey SigningKey(IConfiguration config)
    {
        var key = config["Auth:DevSigningKey"];
        if (string.IsNullOrEmpty(key) || Encoding.UTF8.GetByteCount(key) < 32)
        {
            throw new InvalidOperationException("Auth:DevSigningKey must be at least 32 bytes when Auth:Authority is not set.");
        }
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
    }

    public static string Create(IConfiguration config, UserRow user, TimeSpan? lifetime = null)
    {
        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Expires = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromHours(8)),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = user.UserId.ToString(),
                ["tid"] = user.TenantId.ToString(),
                ["role"] = user.Role,
                ["name"] = user.DisplayName,
                ["email"] = user.Email,
            },
            SigningCredentials = new SigningCredentials(SigningKey(config), SecurityAlgorithms.HmacSha256),
        });
    }
}
