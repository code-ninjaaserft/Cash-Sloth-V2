using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CashSloth.Contracts;
using CashSloth.Server.Data;
using CashSloth.Server.Security;
using Microsoft.IdentityModel.Tokens;

namespace CashSloth.Server.Tests;

public sealed class CryptographyTests
{
    [Fact]
    public async Task Es256Token_ValidatesAndRejectsTampering()
    {
        await using var context = await TestServerContext.CreateAsync();
        var service = context.GetRequiredService<TokenService>();
        var user = new ServerUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = "cashier",
            IsActive = true,
            IsApproved = true
        };
        var token = service.CreateAccessToken(user, CashSlothRoles.User, Guid.NewGuid(), Guid.NewGuid());
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(token.Token, service.CreateValidationParameters(), out var validated);
        Assert.Equal(SecurityAlgorithms.EcdsaSha256, ((JwtSecurityToken)validated).Header.Alg);
        Assert.Equal(user.Id, principal.FindFirst("sub")?.Value);

        var replacement = token.Token[^1] == 'A' ? 'B' : 'A';
        var tampered = token.Token[..^1] + replacement;
        Assert.ThrowsAny<SecurityTokenException>(() =>
            handler.ValidateToken(tampered, service.CreateValidationParameters(), out _));
    }

    [Fact]
    public async Task ExpiredToken_IsRejected_AndTrustFingerprintMatchesKey()
    {
        await using var context = await TestServerContext.CreateAsync();
        var keys = context.GetRequiredService<ServerKeyService>();
        var trust = keys.CreateTrustDocument("https://api.example.test");
        Assert.Equal(keys.Fingerprint, trust.Fingerprint);
        Assert.Equal(keys.KeyId, trust.KeyId);

        var now = DateTime.UtcNow;
        var expired = new JwtSecurityToken(
            issuer: $"cashsloth-server:{keys.ServerId}",
            audience: TokenService.Audience,
            claims: [new Claim("sub", "user")],
            notBefore: now.AddHours(-2),
            expires: now.AddHours(-1),
            signingCredentials: new SigningCredentials(keys.SecurityKey, SecurityAlgorithms.EcdsaSha256));
        var encoded = new JwtSecurityTokenHandler().WriteToken(expired);
        Assert.Throws<SecurityTokenExpiredException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(encoded, new TokenService(keys).CreateValidationParameters(), out _));
    }
}
