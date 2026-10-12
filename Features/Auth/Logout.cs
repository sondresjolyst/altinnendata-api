using altinnendata_api.Infrastructure;
using SjolystInnovation.AppKit.Auth;

namespace altinnendata_api.Features.Auth
{
    /// <summary>Signs out by deleting the refresh token. It answers the same whether or not the token was live.</summary>
    public static class Logout
    {
        public static async Task<IResult> Handle(LogoutRequestDto req, IRefreshTokenService refreshTokens)
        {
            await refreshTokens.RevokeAsync(req.RefreshToken);
            return TypedResults.NoContent();
        }

        public class Endpoint : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app) =>
                app.MapPost("/api/auth/logout", Handle).AllowAnonymous().WithValidation<LogoutRequestDto>();
        }
    }
}
