using System.Net;

namespace altinnendata_api.Services
{
    public static class AuthEmailTemplates
    {
        public static string PasswordReset(string companyName, string? firstName, string code)
        {
            string Enc(string? v) => WebUtility.HtmlEncode(v ?? string.Empty);
            var greeting = string.IsNullOrWhiteSpace(firstName) ? "Hi there," : $"Hi {Enc(firstName)},";
            return $@"
<h2>Reset your password</h2>
<p>{greeting}</p>
<p>We received a request to reset your {Enc(companyName)} password. Use the code below to continue:</p>
<p style=""font-size:24px;font-weight:bold;letter-spacing:3px"">{Enc(code)}</p>
<p>This code expires in 30 minutes. If you didn't request a password reset, you can ignore this email — your password won't change.</p>";
        }

        public static string Invite(string companyName, string? firstName, string code)
        {
            string Enc(string? v) => WebUtility.HtmlEncode(v ?? string.Empty);
            var greeting = string.IsNullOrWhiteSpace(firstName) ? "Hi there," : $"Hi {Enc(firstName)},";
            return $@"
<h2>Set your password</h2>
<p>{greeting}</p>
<p>An account has been created for you at {Enc(companyName)}. Open the password page, enter your email and the code below, and choose a password:</p>
<p style=""font-size:24px;font-weight:bold;letter-spacing:3px"">{Enc(code)}</p>
<p>This code expires in 7 days.</p>";
        }
    }
}
