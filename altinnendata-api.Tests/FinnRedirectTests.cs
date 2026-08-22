using System.Net;
using altinnendata_api.Features.Finn;
using Xunit;

namespace altinnendata_api.Tests;

public class FinnRedirectTests
{
    /// <summary>Answers each url from a script of status codes and Location headers.</summary>
    private sealed class ScriptedHandler(Dictionary<string, (HttpStatusCode Status, string? Location)> script)
        : HttpMessageHandler
    {
        public readonly List<string> Requested = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);

            var (status, location) = script.TryGetValue(url, out var step)
                ? step
                : (HttpStatusCode.OK, null);

            var response = new HttpResponseMessage(status) { RequestMessage = request };
            if (location != null) response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
            return Task.FromResult(response);
        }
    }

    private const string Short = "https://www.finn.no/456796861";
    private const string Advert = "https://www.finn.no/recommerce/forsale/item/456796861";

    private static HttpClient Client(ScriptedHandler handler) => new(handler);

    [Fact]
    public async Task Follows_TheShortLinkTheFinnAppProduces()
    {
        var handler = new ScriptedHandler(new() { [Short] = (HttpStatusCode.MovedPermanently, Advert) });

        using var response = await ImportFinnAd.FollowAsync(Client(handler), Short, TestContext.Current.CancellationToken);

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Advert, response.RequestMessage!.RequestUri!.ToString());
        Assert.Equal([Short, Advert], handler.Requested);
    }

    [Fact]
    public async Task Returns_TheAdvertDirectlyWhenThereIsNoRedirect()
    {
        var handler = new ScriptedHandler([]);

        using var response = await ImportFinnAd.FollowAsync(Client(handler), Advert, TestContext.Current.CancellationToken);

        Assert.NotNull(response);
        Assert.Equal(Advert, response.RequestMessage!.RequestUri!.ToString());
        Assert.Single(handler.Requested);
    }

    /// <summary>The reason the handler does not follow redirects itself.</summary>
    [Theory]
    [InlineData("https://evil.example.com/advert")]
    [InlineData("http://www.finn.no/recommerce/forsale/item/1")]
    [InlineData("http://10.0.0.1/metadata")]
    public async Task Refuses_ARedirectThatLeavesFinnOverHttps(string target)
    {
        var handler = new ScriptedHandler(new() { [Short] = (HttpStatusCode.Found, target) });

        Assert.Null(await ImportFinnAd.FollowAsync(Client(handler), Short, TestContext.Current.CancellationToken));
        Assert.Equal([Short], handler.Requested);
    }

    [Fact]
    public async Task Resolves_ARelativeLocationAgainstTheCurrentUrl()
    {
        var handler = new ScriptedHandler(new()
        {
            [Short] = (HttpStatusCode.MovedPermanently, "/recommerce/forsale/item/456796861"),
        });

        using var response = await ImportFinnAd.FollowAsync(Client(handler), Short, TestContext.Current.CancellationToken);

        Assert.NotNull(response);
        Assert.Equal(Advert, response.RequestMessage!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Gives_UpOnAChainThatNeverArrives()
    {
        var handler = new ScriptedHandler(new() { [Short] = (HttpStatusCode.Found, Short) });

        Assert.Null(await ImportFinnAd.FollowAsync(Client(handler), Short, TestContext.Current.CancellationToken));
        Assert.InRange(handler.Requested.Count, 2, 10);
    }

    [Fact]
    public async Task Passes_AnErrorStatusBackRatherThanTreatingItAsARedirect()
    {
        var handler = new ScriptedHandler(new() { [Advert] = (HttpStatusCode.NotFound, null) });

        using var response = await ImportFinnAd.FollowAsync(Client(handler), Advert, TestContext.Current.CancellationToken);

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
