using FluentValidation;
using altinnendata_api.Models;

namespace altinnendata_api.Features.Contact
{
    public record AssigneeDto(string Id, string Name);

    public record EnquiryCountDto(int New);

    public record EnquirySummaryDto(
        int Id,
        DateTime CreatedAt,
        string Name,
        string? BuildTitle,
        string? UseCase,
        EnquiryStatus Status,
        AssigneeDto? Owner,
        bool OwnerRemoved);

    public record EnquiryDto(
        int Id,
        DateTime CreatedAt,
        string Name,
        string? BuildTitle,
        string? UseCase,
        EnquiryStatus Status,
        AssigneeDto? Owner,
        bool OwnerRemoved,
        string Email,
        string? Phone,
        string? BudgetNok,
        string? BuildSlug,
        string Locale,
        string Message,
        bool ReceiptSent,
        DateTime? NoticeSentAt,
        DateTime UpdatedAt);

    public record EnquiryPage(IReadOnlyList<EnquirySummaryDto> Items, int Total);

    /// <summary>
    /// A change to an enquiry. The owner changes only when <paramref name="SetOwner"/> is true and
    /// <paramref name="ExpectedAssignedUserId"/> is the owner the client saw, so a take-over never overwrites a newer one.
    /// </summary>
    public record UpdateEnquiryRequest(
        EnquiryStatus? Status,
        bool SetOwner,
        string? AssignedUserId,
        string? ExpectedAssignedUserId);

    public class UpdateEnquiryRequestValidator : AbstractValidator<UpdateEnquiryRequest>
    {
        public UpdateEnquiryRequestValidator()
        {
            RuleFor(x => x.Status).IsInEnum();
            RuleFor(x => x).Must(x => x.Status != null || x.SetOwner)
                .WithMessage("Set a status or an owner.");
        }
    }
}
