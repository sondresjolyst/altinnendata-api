using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;

namespace altinnendata_api.Features.Components
{
    /// <summary>Create / update / delete a catalog part, and list parts filtered by category.</summary>
    public static class Parts
    {
        public static async Task<IResult> GetAll(ApplicationDbContext db, CancellationToken ct, int? categoryId = null)
        {
            var query = db.ComponentParts
                .AsNoTracking()
                .Include(p => p.Category)
                .Include(p => p.Manufacturer)
                .AsQueryable();

            if (categoryId.HasValue)
                query = query.Where(p => p.CategoryId == categoryId.Value);

            var parts = await query
                .OrderBy(p => p.Category!.SortOrder)
                .ThenBy(p => p.Manufacturer!.Name)
                .ThenBy(p => p.Name)
                .ToListAsync(ct);

            return TypedResults.Ok(parts.Select(ToDto));
        }

        public static async Task<IResult> Create(PartInput body, ApplicationDbContext db, CancellationToken ct)
        {
            if (!await db.ComponentCategories.AnyAsync(c => c.Id == body.CategoryId, ct))
                return TypedResults.Problem("Unknown category.", statusCode: StatusCodes.Status400BadRequest);

            if (body.ManufacturerId.HasValue && !await db.ComponentManufacturers.AnyAsync(m => m.Id == body.ManufacturerId, ct))
                return TypedResults.Problem("Unknown manufacturer.", statusCode: StatusCodes.Status400BadRequest);

            var name = body.Name.Trim();
            var duplicate = await db.ComponentParts.AnyAsync(
                p => p.CategoryId == body.CategoryId && p.ManufacturerId == body.ManufacturerId && p.Name.ToLower() == name.ToLower(), ct);
            if (duplicate)
                return TypedResults.Problem("That part already exists in this category.", statusCode: StatusCodes.Status409Conflict);

            var part = new ComponentPart
            {
                CategoryId = body.CategoryId,
                ManufacturerId = body.ManufacturerId,
                Name = name,
                Details = string.IsNullOrWhiteSpace(body.Details) ? null : body.Details.Trim()
            };

            db.ComponentParts.Add(part);
            await db.SaveChangesAsync(ct);

            return TypedResults.Ok(ToDto(await LoadAsync(part.Id, db, ct)));
        }

        public static async Task<IResult> Update(int id, PartInput body, ApplicationDbContext db, CancellationToken ct)
        {
            var part = await db.ComponentParts.FindAsync([id], ct);
            if (part == null) return TypedResults.NotFound();

            if (!await db.ComponentCategories.AnyAsync(c => c.Id == body.CategoryId, ct))
                return TypedResults.Problem("Unknown category.", statusCode: StatusCodes.Status400BadRequest);

            var name = body.Name.Trim();
            var duplicate = await db.ComponentParts.AnyAsync(
                p => p.Id != id && p.CategoryId == body.CategoryId && p.ManufacturerId == body.ManufacturerId && p.Name.ToLower() == name.ToLower(), ct);
            if (duplicate)
                return TypedResults.Problem("That part already exists in this category.", statusCode: StatusCodes.Status409Conflict);

            part.CategoryId = body.CategoryId;
            part.ManufacturerId = body.ManufacturerId;
            part.Name = name;
            part.Details = string.IsNullOrWhiteSpace(body.Details) ? null : body.Details.Trim();

            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(ToDto(await LoadAsync(part.Id, db, ct)));
        }

        public static async Task<IResult> Delete(int id, ApplicationDbContext db, CancellationToken ct)
        {
            var part = await db.ComponentParts.FindAsync([id], ct);
            if (part == null) return TypedResults.NotFound();

            db.ComponentParts.Remove(part);
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        }

        private static async Task<ComponentPart> LoadAsync(int id, ApplicationDbContext db, CancellationToken ct) =>
            await db.ComponentParts
                .AsNoTracking()
                .Include(p => p.Category)
                .Include(p => p.Manufacturer)
                .FirstAsync(p => p.Id == id, ct);

        internal static PartDto ToDto(ComponentPart part) => new(
            part.Id,
            part.CategoryId,
            part.Category?.Key ?? string.Empty,
            part.ManufacturerId,
            part.Manufacturer?.Name,
            part.Name,
            part.Details);

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app)
            {
                app.MapGet("/api/components/parts", GetAll).AllowAnonymous();

                var admin = app.MapGroup("/api/components/parts").RequireAuthorization(Policies.Admin);
                admin.MapPost("", Create).WithValidation<PartInput>();
                admin.MapPut("{id:int}", Update).WithValidation<PartInput>();
                admin.MapDelete("{id:int}", Delete);
            }
        }
    }
}
