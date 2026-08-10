using Microsoft.AspNetCore.Identity;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;

namespace altinnendata_api.Features.Auth
{
    /// <summary>Logs in a user and issues a JWT + rotating refresh token.</summary>
    public static class Login
    {
        public static async Task<IResult> Handle(LoginModel login, UserManager<User> users, SignInManager<User> signIn, IConfiguration config, ApplicationDbContext db)
        {
            var user = await users.FindByEmailAsync(login.Email);
            if (user == null || user.IsDeleted)
                return Unauthorized("Invalid email or password.");

            var result = await signIn.PasswordSignInAsync(user.UserName ?? string.Empty, login.Password, false, lockoutOnFailure: true);
            if (!result.Succeeded)
                return Unauthorized(result.IsLockedOut
                    ? "Too many failed attempts — this login is temporarily locked. Try again later."
                    : "Invalid email or password.");

            var roles = await users.GetRolesAsync(user);
            var token = JwtTokens.BuildJwt(user, roles, config);
            var raw = await JwtTokens.IssueRefreshTokenAsync(db, user.Id);
            await db.SaveChangesAsync();

            return TypedResults.Ok(new TokenResponse(token, raw));
        }

        private static IResult Unauthorized(string message) =>
            TypedResults.Json(new MessageResponse(message), statusCode: StatusCodes.Status401Unauthorized);

        public class Endpoint : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app) =>
                app.MapPost("/api/auth/login", Handle).AllowAnonymous().WithValidation<LoginModel>();
        }
    }
}
