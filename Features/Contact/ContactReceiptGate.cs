namespace altinnendata_api.Features.Contact
{
    /// <summary>
    /// Decides whether the contact form may send a receipt. One receipt per mailbox in <see cref="AddressWindow"/>,
    /// where spellings of the same mailbox count as one, and at most <see cref="GlobalLimit"/> receipts in
    /// <see cref="GlobalWindow"/> across all addresses. The owner's copy of an enquiry is never held back by this.
    /// </summary>
    public sealed class ContactReceiptGate(TimeProvider time)
    {
        public static readonly TimeSpan AddressWindow = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan GlobalWindow = TimeSpan.FromHours(1);
        public const int GlobalLimit = 30;

        private static readonly string[] GmailDomains = ["gmail.com", "googlemail.com"];

        private readonly object _lock = new();
        private readonly Dictionary<string, DateTimeOffset> _lastByMailbox = [];
        private readonly Queue<DateTimeOffset> _sent = new();

        /// <summary>True when a receipt to <paramref name="address"/> may go out now. The send is counted at once.</summary>
        public bool TryAcquire(string address)
        {
            var now = time.GetUtcNow();
            var mailbox = Mailbox(address);
            lock (_lock)
            {
                while (_sent.Count > 0 && now - _sent.Peek() >= GlobalWindow) _sent.Dequeue();
                foreach (var stale in _lastByMailbox.Where(p => now - p.Value >= AddressWindow).Select(p => p.Key).ToList())
                    _lastByMailbox.Remove(stale);

                if (_lastByMailbox.ContainsKey(mailbox) || _sent.Count >= GlobalLimit) return false;

                _lastByMailbox[mailbox] = now;
                _sent.Enqueue(now);
                return true;
            }
        }

        /// <summary>
        /// The mailbox an address delivers to, in lower case and without a +tag. Gmail ignores dots in the local part
        /// and treats googlemail.com as gmail.com, so those are folded too.
        /// </summary>
        public static string Mailbox(string address)
        {
            var trimmed = address.Trim().ToLowerInvariant();
            var at = trimmed.LastIndexOf('@');
            if (at <= 0) return trimmed;

            var local = trimmed[..at];
            var domain = trimmed[(at + 1)..];
            var plus = local.IndexOf('+');
            if (plus > 0) local = local[..plus];
            if (GmailDomains.Contains(domain))
            {
                local = local.Replace(".", "");
                domain = "gmail.com";
            }
            return $"{local}@{domain}";
        }
    }
}
