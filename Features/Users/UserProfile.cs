using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Helpers;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;

namespace altinnendata_api.Features.Users
{
    /// <summary>Self-serve account: profile read/update, data export, account deletion.</summary>
    public static class UserProfile
    {
        private static bool CanActOn(HttpContext http, string id) =>
            http.User.UserId() == id || http.User.IsInRole(RoleNames.Admin);

        private static UserProfileDto ToProfile(User u) =>
            new(u.Id, u.UserName ?? "", u.FirstName, u.LastName, u.Email ?? "", u.CreatedAt);

        public static async Task<IResult> Get(string id, HttpContext http, UserManager<User> users)
        {
            if (!CanActOn(http, id)) return TypedResults.Forbid();
            var user = await users.FindByIdAsync(id);
            if (user == null || user.IsDeleted) return TypedResults.NotFound();
            return TypedResults.Ok(ToProfile(user));
        }

        public static async Task<IResult> Update(string id, UpdateProfileDto dto, HttpContext http, UserManager<User> users)
        {
            if (!CanActOn(http, id)) return TypedResults.Forbid();
            var user = await users.FindByIdAsync(id);
            if (user == null || user.IsDeleted) return TypedResults.NotFound();

            user.FirstName = dto.FirstName;
            user.LastName = dto.LastName;
            var result = await users.UpdateAsync(user);
            if (!result.Succeeded) return TypedResults.Problem("Failed to update profile.", statusCode: StatusCodes.Status400BadRequest);

            return TypedResults.Ok(ToProfile(user));
        }

        public static async Task<IResult> ChangePassword(string id, ChangePasswordDto dto, HttpContext http, UserManager<User> users)
        {
            if (http.User.UserId() != id) return TypedResults.Forbid();
            var user = await users.FindByIdAsync(id);
            if (user == null || user.IsDeleted) return TypedResults.NotFound();

            var result = await users.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);
            if (!result.Succeeded)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["NewPassword"] = result.Errors.Select(e => e.Description).ToArray()
                });

            return TypedResults.Ok(new MessageResponse("Password changed."));
        }

        public static async Task<IResult> Export(string id, HttpContext http, UserManager<User> users)
        {
            if (!CanActOn(http, id)) return TypedResults.Forbid();
            var user = await users.FindByIdAsync(id);
            if (user == null || user.IsDeleted) return TypedResults.NotFound();

            var roles = await users.GetRolesAsync(user);
            return TypedResults.Ok(new UserExport(ToProfile(user), roles, DateTime.UtcNow));
        }

        public static async Task<IResult> Delete(string id, HttpContext http, UserManager<User> users, ApplicationDbContext db)
        {
            if (!CanActOn(http, id)) return TypedResults.Forbid();
            var user = await users.FindByIdAsync(id);
            if (user == null || user.IsDeleted) return TypedResults.NotFound();

            // There is no public sign-up, so losing the last admin locks everyone out for good.
            if (await users.IsInRoleAsync(user, RoleNames.Admin))
            {
                var admins = await users.GetUsersInRoleAsync(RoleNames.Admin);
                if (admins.Count(a => !a.IsDeleted) <= 1)
                    return TypedResults.Problem("This is the last admin account; give someone else the Admin role first.",
                        statusCode: StatusCodes.Status400BadRequest);
            }

            user.IsDeleted = true;
            user.DeletedAt = DateTime.UtcNow;
            user.FirstName = "Deleted";
            user.LastName = "User";
            await users.SetEmailAsync(user, $"deleted-{user.Id}@deleted.invalid");
            await users.SetUserNameAsync(user, $"deleted-{user.Id}");
            await users.UpdateAsync(user);
            await users.RemovePasswordAsync(user);

            var liveTokens = await db.RefreshTokens.Where(t => t.UserId == user.Id && t.Revoked == null).ToListAsync();
            var now = DateTime.UtcNow;
            foreach (var token in liveTokens) token.Revoked = now;
            await db.SaveChangesAsync();

            return TypedResults.NoContent();
        }

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app)
            {
                var group = app.MapGroup("/api/users").RequireAuthorization();
                group.MapGet("{id}/profile", Get);
                group.MapPut("{id}/profile", Update).WithValidation<UpdateProfileDto>();
                group.MapPut("{id}/password", ChangePassword).WithValidation<ChangePasswordDto>();
                group.MapGet("{id}/export", Export);
                group.MapDelete("{id}/account", Delete);
            }
        }
    }
}
