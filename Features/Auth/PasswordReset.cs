using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Models.Admin;
using altinnendata_api.Services;

namespace altinnendata_api.Features.Auth
{
    /// <summary>Code-based password reset: email a short code, verify it, set a new password.</summary>
    public static class PasswordReset
    {
        private const string RequestGeneric = "If this email is registered, a reset code has been sent.";
        private const string InvalidGeneric = "Invalid or expired reset code.";

        public static async Task<IResult> Request(RequestPasswordResetDto dto, ApplicationDbContext db, UserManager<User> users, IEmailService email, CancellationToken ct)
        {
            var user = await users.FindByEmailAsync(dto.Email);
            if (user != null && !user.IsDeleted)
            {
                var code = GenerateCode();
                user.PasswordResetCodeHash = HashText(code);
                user.PasswordResetCodeExpiration = DateTime.UtcNow.AddMinutes(30);
                user.PasswordResetAttempts = 0;
                await users.UpdateAsync(user);

                var settings = await db.AppSettings.FindAsync([1], ct) ?? new AppSettings();
                await email.SendEmailAsync(user.Email!, "Password reset code",
                    AuthEmailTemplates.PasswordReset(settings.CompanyName, user.FirstName, code));
            }

            return TypedResults.Ok(new MessageResponse(RequestGeneric));
        }

        public static async Task<IResult> Reset(ResetPasswordDto dto, UserManager<User> users)
        {
            var user = await users.FindByEmailAsync(dto.Email);
            if (user == null || user.IsDeleted)
                return TypedResults.Problem(InvalidGeneric, statusCode: StatusCodes.Status400BadRequest);

            if (user.PasswordResetAttempts >= 5)
                return TypedResults.Problem("Too many attempts. Request a new reset code.", statusCode: StatusCodes.Status429TooManyRequests);

            if (user.PasswordResetCodeHash != HashText(dto.Code) || user.PasswordResetCodeExpiration < DateTime.UtcNow)
            {
                user.PasswordResetAttempts++;
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

            user.PasswordResetCodeHash = null;
            user.PasswordResetCodeExpiration = null;
            user.PasswordResetAttempts = 0;
            await users.UpdateAsync(user);

            return TypedResults.Ok(new MessageResponse("Password reset successfully."));
        }

        private static string GenerateCode()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var result = new char[6];
            for (int i = 0; i < result.Length; i++)
                result[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
            return new string(result);
        }

        private static string HashText(string text)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            return Convert.ToBase64String(hash);
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
