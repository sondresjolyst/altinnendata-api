using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using altinnendata_api.Models;
using altinnendata_api.Models.Auth;

namespace altinnendata_api.Features.Auth
{
    /// <summary>JWT issuance + refresh-token rotation/hashing shared by the auth slices.</summary>
    internal static class JwtTokens
    {
        public static string BuildJwt(User user, IList<string> roles, IConfiguration config)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(config["Jwt:Key"] ?? string.Empty);
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id),
                new(JwtRegisteredClaimNames.UniqueName, user.UserName ?? string.Empty),
                new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty)
            };
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(1),
                NotBefore = DateTime.UtcNow.AddSeconds(-5),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature),
                Issuer = config["Jwt:Issuer"],
                Audience = config["Jwt:Issuer"]
            };
            return tokenHandler.WriteToken(tokenHandler.CreateToken(tokenDescriptor));
        }

        public static async Task<string> IssueRefreshTokenAsync(ApplicationDbContext db, string userId)
        {
            var rawToken = GenerateRefreshToken();
            var hashedToken = HashText(rawToken);

            var userTokens = await db.RefreshTokens
                .Where(t => t.UserId == userId && t.Revoked == null && t.Expires > DateTime.UtcNow)
                .OrderBy(t => t.Created)
                .ToListAsync();
            if (userTokens.Count >= 5)
                db.RefreshTokens.Remove(userTokens.First());

            db.RefreshTokens.Add(new RefreshToken
            {
                Token = hashedToken,
                UserId = userId,
                Expires = DateTime.UtcNow.AddMonths(6),
                Created = DateTime.UtcNow
            });
            return rawToken;
        }

        public static string HashText(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            var hash = SHA256.HashData(bytes);
            return Convert.ToBase64String(hash);
        }

        private static string GenerateRefreshToken()
        {
            var randomNumber = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomNumber);
            return Convert.ToBase64String(randomNumber);
        }

        public static ClaimsPrincipal? GetPrincipalFromExpiredToken(string token, IConfiguration config)
        {
            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = false,
                ValidateIssuer = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"] ?? string.Empty)),
                ValidateLifetime = false
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            try
            {
                var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out var securityToken);
                if (securityToken is JwtSecurityToken jwt &&
                    jwt.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
                    return principal;
            }
            catch
            {
                return null;
            }
            return null;
        }
    }
}
