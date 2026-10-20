using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CloudDrive.Server.Data;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CloudDrive.Server.Auth;

public class TokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _o = options.Value;

    public string Create(User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.UserName)
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_o.Key));
        var token = new JwtSecurityToken(
            _o.Issuer, _o.Audience, claims,
            expires: DateTime.UtcNow.AddMinutes(_o.ExpiresMinutes),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
