using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Helpers;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;

namespace altinnendata_api.Features.Users
{
    /// <summary>Admin: list users and assign / revoke roles.</summary>
    public static class UserRoles
    {
        public static async Task<IResult> GetUsers(ApplicationDbContext db, UserManager<User> users, bool includeDeleted = false)
        {
            var query = db.Users.AsQueryable();
            if (!includeDeleted) query = query.Where(u => !u.IsDeleted);
            var list = await query.OrderBy(u => u.Email).ToListAsync();

            var dtos = new List<AdminUserDto>();
            foreach (var u in list)
                dtos.Add(new AdminUserDto(u.Id, u.UserName ?? "", u.Email ?? "", u.FirstName, u.LastName, u.CreatedAt, u.IsDeleted, await users.GetRolesAsync(u)));
            return TypedResults.Ok(dtos);
        }

        public static async Task<IResult> AddRole(string id, AssignRoleDto dto, UserManager<User> users)
        {
            if (!RoleNames.AllRoles.Contains(dto.Role)) return TypedResults.BadRequest(new MessageResponse("Unknown role."));
            var user = await users.FindByIdAsync(id);
            if (user == null || user.IsDeleted) return TypedResults.NotFound();
            if (await users.IsInRoleAsync(user, dto.Role)) return TypedResults.Ok(new MessageResponse("User already has this role."));

            var result = await users.AddToRoleAsync(user, dto.Role);
            if (!result.Succeeded) return TypedResults.BadRequest(new MessageResponse("Failed to assign role."));
            return TypedResults.Ok(new MessageResponse("Role assigned."));
        }

        public static async Task<IResult> RemoveRole(string id, string role, HttpContext http, UserManager<User> users)
        {
            if (!RoleNames.AllRoles.Contains(role)) return TypedResults.BadRequest(new MessageResponse("Unknown role."));
            if (role == RoleNames.Admin && id == http.User.UserId())
                return TypedResults.BadRequest(new MessageResponse("You cannot remove your own admin role."));

            var user = await users.FindByIdAsync(id);
            if (user == null) return TypedResults.NotFound();

            var result = await users.RemoveFromRoleAsync(user, role);
            if (!result.Succeeded) return TypedResults.BadRequest(new MessageResponse("Failed to remove role."));
            return TypedResults.NoContent();
        }

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app)
            {
                var group = app.MapGroup("/api/users").RequireAuthorization(Policies.Admin);
                group.MapGet("", GetUsers);
                group.MapPost("{id}/roles", AddRole).WithValidation<AssignRoleDto>();
                group.MapDelete("{id}/roles/{role}", RemoveRole);
            }
        }
    }
}
