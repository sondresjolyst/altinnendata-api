namespace altinnendata_api.Services
{
    /// <summary>
    /// Thrown for client-correctable problems (bad input, unsupported file). Mapped to a 400 by the
    /// global exception handler; its message is safe to return to the caller.
    /// </summary>
    public sealed class AppValidationException : Exception
    {
        public AppValidationException(string message) : base(message) { }
    }
}
