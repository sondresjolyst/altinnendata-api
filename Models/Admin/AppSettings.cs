using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using altinnendata_api.Helpers;

namespace altinnendata_api.Models.Admin
{
    public class AppSettings
    {
        public int Id { get; set; } = 1;

        [MaxLength(200)]
        [EmailAddress]
        public string ContactRecipientEmail { get; set; } = "sonyslyst@gmail.com";

        [MaxLength(100)]
        public string CompanyName { get; set; } = "Altinnendata";

        /// <summary>Registered name. Empty until the business is registered in Enhetsregisteret; falls back to CompanyName.</summary>
        [MaxLength(200)]
        public string CompanyLegalName { get; set; } = "";

        /// <summary>Organisasjonsnummer. Empty until registered; legal pages hide the field while it is blank.</summary>
        [MaxLength(50)]
        public string OrgNumber { get; set; } = "";

        public bool VatRegistered { get; set; }

        [MaxLength(200)]
        public string StreetAddress { get; set; } = "Mårvegen 21a";

        /// <summary>Postnummer. Kept apart from the street so structured data can expose it as its own field.</summary>
        [MaxLength(20)]
        public string PostalCode { get; set; } = "4347";

        /// <summary>Poststed.</summary>
        [MaxLength(100)]
        public string AddressLocality { get; set; } = "Lye";

        /// <summary>Fylke. Optional; omitted from structured data while blank.</summary>
        [MaxLength(100)]
        public string AddressRegion { get; set; } = "";

        /// <summary>The address as one line, for display. Derived, never stored.</summary>
        [NotMapped]
        public string FormattedAddress => PostalAddress.Format(StreetAddress, PostalCode, AddressLocality);

        [MaxLength(200)]
        public string PublicEmail { get; set; } = "altinnendata@gmail.com";

        [MaxLength(40)]
        public string PublicPhone { get; set; } = "+47 473 88 759";

        public string? LogoData { get; set; }

        [MaxLength(50)]
        public string? LogoContentType { get; set; }

        public string? IconData { get; set; }

        [MaxLength(50)]
        public string? IconContentType { get; set; }
    }
}
