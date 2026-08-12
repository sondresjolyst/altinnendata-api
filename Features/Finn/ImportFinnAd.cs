using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using altinnendata_api.Constants;
using altinnendata_api.Helpers;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Services;

namespace altinnendata_api.Features.Finn
{
    public record FinnImportRequest(string Url);

    public record FinnImportResponse(
        string Url,
        string? Title,
        string? Summary,
        string? Description,
        int? PriceNok,
        string? CoverImageId,
        IReadOnlyList<string> ImageIds,
        int SkippedImages);

    public class FinnImportValidator : AbstractValidator<FinnImportRequest>
    {
        public FinnImportValidator()
        {
            RuleFor(x => x.Url)
                .NotEmpty()
                .MaximumLength(400)
                .Must(FinnUrls.IsAdUrl)
                .WithMessage("The link must be an https address on finn.no.");
        }
    }

    /// <summary>Admin: reads a finn.no advert and returns values to prefill the build form with.</summary>
    public static class ImportFinnAd
    {
        private const int MaxHtmlBytes = 4 * 1024 * 1024;
        private const int MaxImageBytes = 8 * 1024 * 1024;
        private const int SummaryLength = 400;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

        public static async Task<IResult> Handle(
            FinnImportRequest request,
            IHttpClientFactory clients,
            ApplicationDbContext db,
            IImageStorageService images,
            HttpContext http,
            ILoggerFactory loggerFactory,
            CancellationToken ct)
        {
            var logger = loggerFactory.CreateLogger("Finn");
            using var client = CreateClient(clients);

            string html;
            try
            {
                using var response = await client.GetAsync(request.Url, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode)
                    return TypedResults.Problem($"finn.no answered {(int)response.StatusCode}.", statusCode: StatusCodes.Status502BadGateway);

                html = await ReadCappedAsync(response, MaxHtmlBytes, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning("Could not read the advert {Url}: {Error}", request.Url, ex.Message);
                return TypedResults.Problem("Could not reach finn.no.", statusCode: StatusCodes.Status502BadGateway);
            }

            var ad = FinnAdParser.Parse(html);

            var stored = new List<string>();
            var skipped = 0;
            foreach (var imageUrl in ad.ImageUrls)
            {
                if (!FinnUrls.IsImageUrl(imageUrl)) { skipped++; continue; }

                var id = await StoreImageAsync(client, imageUrl, db, images, http, ct);
                if (id == null) skipped++;
                else stored.Add(id);
            }

            var description = ad.Description;
            var summary = description != null && description.Length > SummaryLength
                ? description[..SummaryLength].TrimEnd()
                : description;

            return TypedResults.Ok(new FinnImportResponse(
                request.Url,
                ad.Title,
                summary,
                description,
                ad.PriceNok,
                stored.FirstOrDefault(),
                stored,
                skipped));
        }

        private static HttpClient CreateClient(IHttpClientFactory clients)
        {
            var client = clients.CreateClient("finn");
            client.Timeout = Timeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("altinnendata-api (+https://www.altinnendata.no)");
            return client;
        }

        private static async Task<string> ReadCappedAsync(HttpResponseMessage response, int maxBytes, CancellationToken ct)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var memory = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                if (memory.Length + read > maxBytes) break;
                memory.Write(buffer, 0, read);
            }
            return System.Text.Encoding.UTF8.GetString(memory.ToArray());
        }

        private static async Task<string?> StoreImageAsync(
            HttpClient client,
            string url,
            ApplicationDbContext db,
            IImageStorageService images,
            HttpContext http,
            CancellationToken ct)
        {
            try
            {
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode) return null;

                var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
                if (response.Content.Headers.ContentLength > MaxImageBytes) return null;

                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length == 0 || bytes.Length > MaxImageBytes) return null;

                var fileName = Path.GetFileName(new Uri(url).AbsolutePath);
                if (string.IsNullOrWhiteSpace(fileName)) fileName = "finn-image";

                var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName)
                {
                    Headers = new HeaderDictionary(),
                    ContentType = contentType,
                };

                var (storedPath, storedType, sizeBytes) = await images.SaveAsync(file, ct);

                var image = new ContentImage
                {
                    FileName = fileName,
                    ContentType = storedType,
                    SizeBytes = sizeBytes,
                    StoredPath = storedPath,
                    UploadedByUserId = http.User.UserId(),
                };
                db.ContentImages.Add(image);
                await db.SaveChangesAsync(ct);

                foreach (var variant in await images.GenerateWebpVariantsAsync(storedPath, ct))
                    db.ContentImageVariants.Add(ContentImageVariant.From(image.Id, variant));
                await db.SaveChangesAsync(ct);

                return image.Id;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
            {
                return null;
            }
        }

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app) =>
                app.MapPost("/api/finn/import", Handle)
                    .RequireAuthorization(Policies.Admin)
                    .WithValidation<FinnImportRequest>();
        }
    }
}
