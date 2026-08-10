using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;

namespace altinnendata_api.Features.Builds
{
    /// <summary>Public read endpoints for PC builds (list, by slug) plus the admin editor's full view.</summary>
    public static class BuildQueries
    {
        private static bool IsAdmin(HttpContext http) => http.User.IsInRole(RoleNames.Admin);

        private static IQueryable<PcBuild> WithGraph(ApplicationDbContext db) =>
            db.PcBuilds
                .AsNoTracking()
                .Include(b => b.Translations)
                .Include(b => b.Components).ThenInclude(c => c.ComponentPart).ThenInclude(p => p!.Manufacturer)
                .Include(b => b.Components).ThenInclude(c => c.ComponentPart).ThenInclude(p => p!.Category).ThenInclude(c => c!.Translations)
                .Include(b => b.Components).ThenInclude(c => c.ComponentCategory).ThenInclude(c => c!.Translations);

        public static async Task<IResult> GetAll(HttpContext http, ApplicationDbContext db, CancellationToken ct,
            string? locale = null, string? availability = null, string? category = null, bool all = false)
        {
            var resolved = Locales.Normalize(locale);
            var includeDrafts = all && IsAdmin(http);

            var query = WithGraph(db);
            if (!includeDrafts)
                query = query.Where(b => b.Published);

            if (!string.IsNullOrWhiteSpace(availability))
            {
                if (!Enum.TryParse<BuildAvailability>(availability, ignoreCase: true, out var parsed))
                    return TypedResults.Problem("Unknown availability filter.", statusCode: StatusCodes.Status400BadRequest);
                query = query.Where(b => b.Availability == parsed);
            }

            if (!string.IsNullOrWhiteSpace(category))
                query = query.Where(b => b.Category == category);

            var builds = await query
                .OrderBy(b => b.SortOrder)
                .ThenByDescending(b => b.BuiltOn)
                .ThenByDescending(b => b.CreatedAt)
                .ToListAsync(ct);

            return TypedResults.Ok(builds.Select(b => BuildMapping.ToSummary(b, resolved)));
        }

        public static async Task<IResult> GetBySlug(string slug, HttpContext http, ApplicationDbContext db, CancellationToken ct, string? locale = null)
        {
            var resolved = Locales.Normalize(locale);
            var build = await WithGraph(db).FirstOrDefaultAsync(b => b.Slug == slug, ct);
            if (build == null || (!build.Published && !IsAdmin(http)))
                return TypedResults.NotFound();

            return TypedResults.Ok(BuildMapping.ToDetail(build, resolved));
        }

        public static async Task<IResult> GetForEdit(int id, ApplicationDbContext db, CancellationToken ct)
        {
            var build = await WithGraph(db).FirstOrDefaultAsync(b => b.Id == id, ct);
            if (build == null) return TypedResults.NotFound();
            return TypedResults.Ok(BuildMapping.ToAdmin(build));
        }

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app)
            {
                var group = app.MapGroup("/api/builds").AllowAnonymous();
                group.MapGet("", GetAll);
                group.MapGet("{id:int}/edit", GetForEdit).RequireAuthorization(Policies.Admin);
                group.MapGet("{slug}", GetBySlug);
            }
        }
    }
}
