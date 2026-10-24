using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;

namespace altinnendata_api.Features.Shop
{
    /// <summary>What the return page may show about an order. No buyer data. <c>BuildSlug</c> names the build that was bought.</summary>
    public record OrderStatusDto(string Status, string Reference, string BuildTitle, int TotalInOre, string DeliveryMethod, string BuildSlug);

    /// <summary>Public order status, found by the buyer's secret token.</summary>
    public static partial class OrderStatusEndpoint
    {
        [GeneratedRegex("^[0-9a-f]{32}$")]
        private static partial Regex TokenPattern();

        public static async Task<IResult> Get(string token, ApplicationDbContext db, CancellationToken ct)
        {
            if (!TokenPattern().IsMatch(token)) return TypedResults.NotFound();

            var order = await db.Orders
                .AsNoTracking()
                .Where(o => o.PublicToken == token)
                .Select(o => new OrderStatusDto(o.Status.ToString(), o.Reference, o.BuildTitle, o.TotalInOre, o.DeliveryMethod.ToString(), o.BuildSlug))
                .FirstOrDefaultAsync(ct);

            return order == null ? TypedResults.NotFound() : TypedResults.Ok(order);
        }

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app)
            {
                app.MapGet("/api/shop/orders/{token}/status",
                        (string token, HttpContext http, ApplicationDbContext db, CancellationToken ct) =>
                        {
                            http.Response.Headers.CacheControl = "no-store";
                            return Get(token, db, ct);
                        })
                    .AllowAnonymous();
            }
        }
    }
}
