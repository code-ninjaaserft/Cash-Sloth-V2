using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CashSloth.Contracts;
using CashSloth.Server.Data;
using Microsoft.IdentityModel.Tokens;

namespace CashSloth.Server.Security;

public sealed class TokenService(ServerKeyService keys)
{
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(12);
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    public (string Token, DateTimeOffset ExpiresAtUtc) CreateAccessToken(
        ServerUser user,
        string role,
        Guid deviceId,
        Guid sessionId)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.Add(AccessTokenLifetime);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName ?? string.Empty),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.UserName ?? string.Empty),
            new Claim(ClaimTypes.Role, role),
            new Claim("device_id", deviceId.ToString("N")),
            new Claim("session_id", sessionId.ToString("N"))
        };

        var jwt = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(keys.SecurityKey, SecurityAlgorithms.EcdsaSha256));
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }

    public TokenValidationParameters CreateValidationParameters() => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = keys.SecurityKey,
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1),
        NameClaimType = ClaimTypes.Name,
        RoleClaimType = ClaimTypes.Role
    };

    public string Issuer => $"cashsloth-server:{keys.ServerId}";
    public const string Audience = "cashsloth-clients";
}
