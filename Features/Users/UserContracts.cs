using FluentValidation;

namespace altinnendata_api.Features.Users
{
    public record UserProfileDto(string Id, string UserName, string FirstName, string LastName, string Email, DateTime CreatedAt);

    public record UpdateProfileDto(string FirstName, string LastName);

    public record ChangePasswordDto(string CurrentPassword, string NewPassword);

    public record UserExport(UserProfileDto Account, IList<string> Roles, DateTime ExportedAt);

    public class UpdateProfileValidator : AbstractValidator<UpdateProfileDto>
    {
        public UpdateProfileValidator()
        {
            RuleFor(x => x.FirstName).NotEmpty().MaximumLength(50);
            RuleFor(x => x.LastName).NotEmpty().MaximumLength(50);
        }
    }

    public class ChangePasswordValidator : AbstractValidator<ChangePasswordDto>
    {
        public ChangePasswordValidator()
        {
            RuleFor(x => x.CurrentPassword).NotEmpty();
            RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(6);
        }
    }
}
