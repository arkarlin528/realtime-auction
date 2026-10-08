using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Bidding.Application.Common;
using Bidding.Domain.Users;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Bidding.Api.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "bidding-api";
    public string Audience { get; set; } = "bidding-web";
    /// <summary>At least 32 characters, from configuration or the Jwt__SigningKey environment variable.</summary>
    public string SigningKey { get; set; } = "";
    public int ExpiryMinutes { get; set; } = 240;

    public SymmetricSecurityKey SecurityKey() => new(Encoding.UTF8.GetBytes(SigningKey));
}

public static class Policies
{
    public const string Admin = nameof(Admin);
}

/// <summary>Uses the wall clock, not the app's TimeProvider: token validation always checks real time.</summary>
internal sealed class JwtTokenService(IOptions<JwtOptions> options) : ITokenService
{
    public (string Token, DateTimeOffset ExpiresAt) CreateToken(User user)
    {
        var jwt = options.Value;
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(jwt.ExpiryMinutes);

        var token = new JwtSecurityToken(
            issuer: jwt.Issuer,
            audience: jwt.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Name, user.DisplayName),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            ],
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(jwt.SecurityKey(), SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public int? UserId => int.TryParse(accessor.HttpContext?.User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id)
        ? id
        : null;

    public UserRole? Role => Enum.TryParse<UserRole>(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.Role), out var role)
        ? role
        : null;
}

/// <summary>SignalR's Clients.User(id) needs to know which claim is the user id. Ours is "sub".</summary>
internal sealed class SubClaimUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirstValue(JwtRegisteredClaimNames.Sub);
}
