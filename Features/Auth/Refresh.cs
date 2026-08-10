using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;

namespace altinnendata_api.Features.Auth
{
    /// <summary>Rotates a refresh token, with reuse detection (revokes the whole family on replay).</summary>
    public static class Refresh
    {
        public static async Task<IResult> Handle(RefreshTokenRequestDto req, UserManager<User> users, IConfiguration config, ApplicationDbContext db)
        {
            var principal = JwtTokens.GetPrincipalFromExpiredToken(req.Token, config);
            if (principal == null) return TypedResults.BadRequest(new MessageResponse("Invalid token"));

            var userId = principal.Claims.FirstOrDefault(x =>
                x.Type == JwtRegisteredClaimNames.Sub || x.Type == ClaimTypes.NameIdentifier)?.Value;
            if (userId == null) return TypedResults.BadRequest(new MessageResponse("Invalid token"));

            var user = await users.FindByIdAsync(userId);
            if (user == null || user.IsDeleted) return Unauthorized("User not found");

            var hashedInput = JwtTokens.HashText(req.RefreshToken);

            await using var tx = await db.Database.BeginTransactionAsync();

            var anyMatch = await db.RefreshTokens.FirstOrDefaultAsync(t => t.Token == hashedInput && t.UserId == user.Id);

            if (anyMatch != null && anyMatch.Revoked != null)
            {
                var now = DateTime.UtcNow;
                var familyLive = await db.RefreshTokens.Where(t => t.UserId == user.Id && t.Revoked == null).ToListAsync();
                foreach (var t in familyLive) t.Revoked = now;
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return Unauthorized("Invalid or expired refresh token");
            }

            var storedToken = anyMatch != null && anyMatch.Revoked == null && anyMatch.Expires > DateTime.UtcNow ? anyMatch : null;
            if (storedToken == null)
            {
                await tx.RollbackAsync();
                return Unauthorized("Invalid or expired refresh token");
            }

            storedToken.Revoked = DateTime.UtcNow;
            var roles = await users.GetRolesAsync(user);
            var newToken = JwtTokens.BuildJwt(user, roles, config);
            var newRaw = await JwtTokens.IssueRefreshTokenAsync(db, user.Id);

            await db.SaveChangesAsync();
            await tx.CommitAsync();

            return TypedResults.Ok(new TokenResponse(newToken, newRaw));
        }

        private static IResult Unauthorized(string message) =>
            TypedResults.Json(new MessageResponse(message), statusCode: StatusCodes.Status401Unauthorized);

        public class Endpoint : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app) =>
                app.MapPost("/api/auth/refresh-token", Handle).AllowAnonymous().WithValidation<RefreshTokenRequestDto>();
        }
    }
}
