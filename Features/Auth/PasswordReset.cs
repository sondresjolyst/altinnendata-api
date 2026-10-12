using Microsoft.AspNetCore.Identity;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Models.Admin;
using altinnendata_api.Services;
using SjolystInnovation.AppKit.Auth;

namespace altinnendata_api.Features.Auth
{
    /// <summary>Code-based password reset: email a short code, verify it, set a new password.</summary>
    public static class PasswordReset
    {
        private const string RequestGeneric = "If this email is registered, a reset code has been sent.";
        private const string InvalidGeneric = "Invalid or expired reset code.";

        public static async Task<IResult> Request(RequestPasswordResetDto dto, ApplicationDbContext db, UserManager<User> users, IEmailService email, IConfiguration config, CancellationToken ct)
        {
            var user = await users.FindByEmailAsync(dto.Email);
            if (user != null && !user.IsDeleted)
            {
                var code = PasswordResetCodes.Issue(user);
                await users.UpdateAsync(user);

                var settings = await db.AppSettings.FindAsync([1], ct) ?? new AppSettings();
                var link = SiteLinks.SetPassword(config, user.Email!, code);
                await email.SendEmailAsync(user.Email!, "Tilbakestill passordet",
                    AuthEmailTemplates.PasswordReset(settings.CompanyName, user.FirstName, code, link));
            }

            return TypedResults.Ok(new MessageResponse(RequestGeneric));
        }

        public static async Task<IResult> Reset(ResetPasswordDto dto, UserManager<User> users)
        {
            var user = await users.FindByEmailAsync(dto.Email);
            if (user == null || user.IsDeleted)
                return TypedResults.Problem(InvalidGeneric, statusCode: StatusCodes.Status400BadRequest);

            switch (PasswordResetCodes.Check(user, dto.Code))
            {
                case CodeCheck.TooManyAttempts:
                    return TypedResults.Problem("Too many attempts. Request a new reset code.", statusCode: StatusCodes.Status429TooManyRequests);
                case CodeCheck.Invalid:
                    await users.UpdateAsync(user);
                    return TypedResults.Problem(InvalidGeneric, statusCode: StatusCodes.Status400BadRequest);
            }

            var token = await users.GeneratePasswordResetTokenAsync(user);
            var result = await users.ResetPasswordAsync(user, token, dto.NewPassword);
            if (!result.Succeeded)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["NewPassword"] = result.Errors.Select(e => e.Description).ToArray()
                });

            PasswordResetCodes.Complete(user);
            await users.UpdateAsync(user);

            return TypedResults.Ok(new MessageResponse("Password reset successfully."));
        }

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app)
            {
                app.MapPost("/api/auth/request-password-reset", Request).AllowAnonymous().WithValidation<RequestPasswordResetDto>();
                app.MapPost("/api/auth/reset-password", Reset).AllowAnonymous().WithValidation<ResetPasswordDto>();
            }
        }
    }
}
