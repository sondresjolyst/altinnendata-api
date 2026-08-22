using altinnendata_api.Infrastructure;

namespace altinnendata_api.Features.Meta
{
    /// <summary>
    /// Tells crawlers to leave this host alone. Nothing here belongs in a search result: the
    /// responses are JSON for the website to render, and it proxies images from its own domain.
    /// </summary>
    public static class RobotsTxt
    {
        private const string Body = "User-agent: *\nDisallow: /\n";

        public static IResult Get() => TypedResults.Text(Body, "text/plain; charset=utf-8");

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app) =>
                app.MapGet("/robots.txt", Get).AllowAnonymous();
        }
    }
}
