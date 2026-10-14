using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using SjolystInnovation.AppKit.Auth;

namespace altinnendata_api.Models
{
    public class User : IdentityUser, IPasswordResetUser, SjolystInnovation.AppKit.Auth.IAppKitAccount
    {
        [Required]
        [MaxLength(50)]
        public required string FirstName { get; set; }

        [Required]
        [MaxLength(50)]
        public required string LastName { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }

        public string? PasswordResetCodeHash { get; set; }
        public DateTime? PasswordResetCodeExpiration { get; set; }
        public int PasswordResetAttempts { get; set; }
    }
}
