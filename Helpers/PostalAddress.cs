namespace altinnendata_api.Helpers
{
    /// <summary>
    /// Norwegian postal addresses, kept as separate parts because structured data needs the
    /// postcode and locality as their own fields. The one-line form is derived, never stored,
    /// so it cannot disagree with the parts.
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
