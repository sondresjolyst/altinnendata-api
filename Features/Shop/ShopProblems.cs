namespace altinnendata_api.Features.Shop
{
    /// <summary>Failures that carry a machine readable <c>code</c> next to the message.</summary>
    public static class ShopProblems
    {
        public static IResult Code(int status, string code, string message) =>
            TypedResults.Problem(
                detail: message,
                statusCode: status,
                extensions: new Dictionary<string, object?> { ["code"] = code, ["message"] = message });
    }
}
