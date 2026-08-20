namespace altinnendata_api.Helpers
{
    /// <summary>
    /// Norwegian postal addresses, kept as separate parts.
    ///
    /// Structured data and address lookups need the postcode and locality as their own fields;
    /// a single free-text line cannot be read that way. The display string is derived from the
    /// parts rather than stored, so the two can never disagree.
    /// </summary>
    public static class PostalAddress
    {
        /// <summary>"Street 1, 4347 Lye" — parts joined, skipping any that are blank.</summary>
        public static string Format(string streetAddress, string postalCode, string addressLocality)
        {
            var place = string.Join(' ', new[] { postalCode, addressLocality }.Where(p => !string.IsNullOrWhiteSpace(p)));
            return string.Join(", ", new[] { streetAddress, place }.Where(p => !string.IsNullOrWhiteSpace(p)));
        }
    }
}
