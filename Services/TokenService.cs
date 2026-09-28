using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using UniflowApi.Common;

namespace UniflowApi.Services;

public interface ITokenService
{
    string GenerateToken(int userId, int? schemeId, string userType, IDictionary<string, string>? extraClaims = null);
}

public class TokenService : ITokenService
{
    private readonly JwtSettings _settings;

    public TokenService(IOptions<JwtSettings> settings) => _settings = settings.Value;

    public string GenerateToken(int userId, int? schemeId, string userType, IDictionary<string, string>? extraClaims = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new("type", userType),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (schemeId.HasValue)
            claims.Add(new Claim("scheme_id", schemeId.Value.ToString()));

        if (extraClaims is not null)
            claims.AddRange(extraClaims.Select(kv => new Claim(kv.Key, kv.Value)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_settings.ExpiryMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
