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

        /// <summary>How many refresh tokens one user may hold at once, across devices and browsers.</summary>
        private const int MaxLiveTokensPerUser = 5;

        public static async Task<string> IssueRefreshTokenAsync(ApplicationDbContext db, string userId)
        {
            var now = DateTime.UtcNow;
            var rawToken = GenerateRefreshToken();
            var hashedToken = HashText(rawToken);

            var liveTokens = (await db.RefreshTokens
                    .Where(t => t.UserId == userId && t.Revoked == null && t.Expires > now)
                    .OrderBy(t => t.Created)
                    .ThenBy(t => t.Id)
                    .ToListAsync())
                // Also filter on the tracked state: the caller may have just revoked the token it
                // is rotating without saving yet, and the database still reports that one live.
                .Where(t => t.Revoked == null)
                .ToList();

            // Drop every token past the cap, not only one. Trimming a single row per issuance left
            // the count permanently above the cap once it had drifted above it.
            //
            // Delete rather than revoke. Rotation marks a consumed token with the same Revoked
            // field, and presenting a revoked token is what Refresh treats as a replay, so an
            // evicted device's next refresh would revoke every session the user has.
            foreach (var stale in liveTokens.SkipLast(MaxLiveTokensPerUser - 1))
                db.RefreshTokens.Remove(stale);

            db.RefreshTokens.Add(new RefreshToken
            {
                Token = hashedToken,
                UserId = userId,
                Expires = now.AddMonths(6),
                Created = now
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
