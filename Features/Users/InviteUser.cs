using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using MapsterMapper;
using Microsoft.AspNetCore.Identity;
using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Models.Admin;
using altinnendata_api.Services;

namespace altinnendata_api.Features.Users
{
    public record InviteUserDto(string Email, string FirstName, string LastName, string Role);

    public class InviteUserValidator : AbstractValidator<InviteUserDto>
    {
        public InviteUserValidator()
        {
            RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
            RuleFor(x => x.FirstName).NotEmpty().MaximumLength(50);
            RuleFor(x => x.LastName).NotEmpty().MaximumLength(50);
            RuleFor(x => x.Role).NotEmpty().Must(RoleNames.AllRoles.Contains).WithMessage("Unknown role.");
        }
    }

    /// <summary>
    /// Admin: create an account without a password and email the invitee a code they use on the
    /// reset-password page to choose one. This is how further admins are added — there is no public sign-up.
    /// </summary>
    public static class InviteUser
    {
        public static async Task<IResult> Handle(InviteUserDto dto, ApplicationDbContext db, UserManager<User> users, IEmailService email, IMapper mapper, CancellationToken ct)
        {
            if (await users.FindByEmailAsync(dto.Email) != null)
                return TypedResults.Problem("That email already has an account.", statusCode: StatusCodes.Status409Conflict);

            var user = mapper.Map<User>(dto);
            user.EmailConfirmed = true;

            var created = await users.CreateAsync(user);
            if (!created.Succeeded)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Email"] = created.Errors.Select(e => e.Description).ToArray()
                });

            await users.AddToRoleAsync(user, dto.Role);

            var code = GenerateCode();
            user.PasswordResetCodeHash = HashText(code);
            user.PasswordResetCodeExpiration = DateTime.UtcNow.AddDays(7);
            user.PasswordResetAttempts = 0;
            await users.UpdateAsync(user);

            var settings = await db.AppSettings.FindAsync([1], ct) ?? new AppSettings();
            await email.SendEmailAsync(user.Email!, $"You have been invited to {settings.CompanyName}",
                AuthEmailTemplates.Invite(settings.CompanyName, user.FirstName, code));

            return TypedResults.Ok(new MessageResponse("Invitation sent."));
        }

        private static string GenerateCode()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var result = new char[6];
            for (var i = 0; i < result.Length; i++)
                result[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
            return new string(result);
        }

        private static string HashText(string text) =>
            Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app) =>
                app.MapPost("/api/users/invite", Handle)
                    .RequireAuthorization(Policies.Admin)
                    .WithValidation<InviteUserDto>();
        }
    }
}
