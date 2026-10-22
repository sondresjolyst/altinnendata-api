namespace altinnendata_api.Models
{
    public enum EnquiryStatus { New, InProgress, Answered, Done }

    /// <summary>
    /// One message sent through the contact form. Deleted 12 months after it was created, whatever its status.
    /// </summary>
    public class Enquiry
    {
        public int Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public required string Name { get; set; }
        public required string Email { get; set; }
        public string? Phone { get; set; }
        public string? UseCase { get; set; }
        public string? BudgetNok { get; set; }

        /// <summary>The build the visitor asked about, copied in so it reads correctly after the build is edited or deleted.</summary>
        public string? BuildSlug { get; set; }
        public string? BuildTitle { get; set; }

        public required string Locale { get; set; }
        public required string Message { get; set; }

        /// <summary>Whether the receipt email to the visitor was sent.</summary>
        public bool ReceiptSent { get; set; }

        /// <summary>When the notice email to the site owner was sent. Null until it is.</summary>
        public DateTime? NoticeSentAt { get; set; }
        public int NoticeAttempts { get; set; }
        public DateTime? NoticeAttemptedAt { get; set; }

        public EnquiryStatus Status { get; set; } = EnquiryStatus.New;

        public string? AssignedUserId { get; set; }
        public User? AssignedUser { get; set; }

        /// <summary>Set when the assigned user was deleted. Their name is never kept.</summary>
        public bool OwnerRemoved { get; set; }
    }
}
