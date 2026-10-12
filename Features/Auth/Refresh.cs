using Microsoft.AspNetCore.Identity;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using SjolystInnovation.AppKit.Auth;

namespace altinnendata_api.Features.Auth
{
    /// <summary>Rotates a refresh token. A replayed token revokes every live token of the user.</summary>
    public static class Refresh
    {
        public static async Task<IResult> Handle(RefreshTokenRequestDto req, UserManager<User> users, IJwtTokenService jwt, IRefreshTokenService refreshTokens)
        {
            var userId = jwt.ReadUserIdFromExpired(req.Token);
            if (userId == null) return TypedResults.BadRequest(new MessageResponse("Invalid token"));

            var user = await users.FindByIdAsync(userId);
            if (user == null || user.IsDeleted) return Unauthorized("User not found");

            var rotation = await refreshTokens.RotateAsync(user.Id, req.RefreshToken);
            if (rotation.Status != RotationStatus.Rotated)
                return Unauthorized("Invalid or expired refresh token");

            var roles = await users.GetRolesAsync(user);
            return TypedResults.Ok(new TokenResponse(jwt.Build(user, roles), rotation.RefreshToken!));
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
