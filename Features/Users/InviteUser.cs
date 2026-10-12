using FluentValidation;
using MapsterMapper;
using Microsoft.AspNetCore.Identity;
using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Models.Admin;
using altinnendata_api.Services;
using SjolystInnovation.AppKit.Auth;

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
        public static async Task<IResult> Handle(InviteUserDto dto, ApplicationDbContext db, UserManager<User> users, IEmailService email, IMapper mapper, IConfiguration config, CancellationToken ct)
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

            var code = PasswordResetCodes.Issue(user, lifetime: TimeSpan.FromDays(7));
            await users.UpdateAsync(user);

            var settings = await db.AppSettings.FindAsync([1], ct) ?? new AppSettings();
            var link = SiteLinks.SetPassword(config, user.Email!, code);
            await email.SendEmailAsync(user.Email!, $"Sett passordet ditt hos {settings.CompanyName}",
                AuthEmailTemplates.Invite(settings.CompanyName, user.FirstName, code, link));

            return TypedResults.Ok(new MessageResponse("Invitation sent."));
        }

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app) =>
                app.MapPost("/api/users/invite", Handle)
                    .RequireAuthorization(Policies.Admin)
                    .WithValidation<InviteUserDto>();
        }
    }
}
