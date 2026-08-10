using System.ComponentModel.DataAnnotations;
using FluentValidation;

namespace altinnendata_api.Features.Auth
{
    public class LoginModel
    {
        [Required][EmailAddress] public required string Email { get; set; }
        [Required] public required string Password { get; set; }
    }

    public class RefreshTokenRequestDto
    {
        [Required] public required string Token { get; set; }
        [Required] public required string RefreshToken { get; set; }
    }

    public record TokenResponse(string Token, string RefreshToken);

    public record RequestPasswordResetDto(string Email);

    public record ResetPasswordDto(string Email, string Code, string NewPassword);

    public class LoginValidator : AbstractValidator<LoginModel>
    {
        public LoginValidator()
        {
            RuleFor(x => x.Email).NotEmpty().EmailAddress();
            RuleFor(x => x.Password).NotEmpty();
        }
    }

    public class RefreshTokenValidator : AbstractValidator<RefreshTokenRequestDto>
    {
        public RefreshTokenValidator()
        {
            RuleFor(x => x.Token).NotEmpty();
            RuleFor(x => x.RefreshToken).NotEmpty();
        }
    }

    public class RequestPasswordResetValidator : AbstractValidator<RequestPasswordResetDto>
    {
        public RequestPasswordResetValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }

    public class ResetPasswordValidator : AbstractValidator<ResetPasswordDto>
    {
        public ResetPasswordValidator()
        {
            RuleFor(x => x.Email).NotEmpty().EmailAddress();
            RuleFor(x => x.Code).NotEmpty();
            RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(6);
        }
    }
}
