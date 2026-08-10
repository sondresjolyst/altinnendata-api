using FluentValidation;

namespace altinnendata_api.Features.Users
{
    public record UserProfileDto(string Id, string UserName, string FirstName, string LastName, string Email, DateTime CreatedAt);

    public record UpdateProfileDto(string FirstName, string LastName);

    public record ChangePasswordDto(string CurrentPassword, string NewPassword);

    public record UserExport(UserProfileDto Account, IList<string> Roles, DateTime ExportedAt);

    public record AdminUserDto(string Id, string UserName, string Email, string FirstName, string LastName, DateTime CreatedAt, bool IsDeleted, IList<string> Roles);

    public record AssignRoleDto(string Role);

    public class UpdateProfileValidator : AbstractValidator<UpdateProfileDto>
    {
        public UpdateProfileValidator()
        {
            RuleFor(x => x.FirstName).NotEmpty().MaximumLength(50);
            RuleFor(x => x.LastName).NotEmpty().MaximumLength(50);
        }
    }

    public class AssignRoleValidator : AbstractValidator<AssignRoleDto>
    {
        public AssignRoleValidator() => RuleFor(x => x.Role).NotEmpty();
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
