using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;

namespace altinnendata_api.Features.Components
{
    /// <summary>Create / rename / delete a component manufacturer (Intel, ASUS, Corsair …).</summary>
    public static class Manufacturers
    {
        public static async Task<IResult> GetAll(ApplicationDbContext db, CancellationToken ct)
        {
            var manufacturers = await db.ComponentManufacturers
                .AsNoTracking()
                .OrderBy(m => m.Name)
                .Select(m => new ManufacturerDto(m.Id, m.Name))
                .ToListAsync(ct);
            return TypedResults.Ok(manufacturers);
        }

        public static async Task<IResult> Create(ManufacturerInput body, ApplicationDbContext db, CancellationToken ct)
        {
            var name = body.Name.Trim();
            var existing = await db.ComponentManufacturers.FirstOrDefaultAsync(m => m.Name.ToLower() == name.ToLower(), ct);
            if (existing != null) return TypedResults.Ok(new ManufacturerDto(existing.Id, existing.Name));

            var manufacturer = new ComponentManufacturer { Name = name };
            db.ComponentManufacturers.Add(manufacturer);
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(new ManufacturerDto(manufacturer.Id, manufacturer.Name));
        }

        public static async Task<IResult> Rename(int id, ManufacturerInput body, ApplicationDbContext db, CancellationToken ct)
        {
            var name = body.Name.Trim();
            var manufacturer = await db.ComponentManufacturers.FindAsync([id], ct);
            if (manufacturer == null) return TypedResults.NotFound();

            if (await db.ComponentManufacturers.AnyAsync(m => m.Id != id && m.Name.ToLower() == name.ToLower(), ct))
                return TypedResults.Problem("A manufacturer with that name already exists.", statusCode: StatusCodes.Status409Conflict);

            manufacturer.Name = name;
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(new ManufacturerDto(manufacturer.Id, manufacturer.Name));
        }

        public static async Task<IResult> Delete(int id, ApplicationDbContext db, CancellationToken ct)
        {
            var manufacturer = await db.ComponentManufacturers.FindAsync([id], ct);
            if (manufacturer == null) return TypedResults.NotFound();

            db.ComponentManufacturers.Remove(manufacturer);
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        }

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app)
            {
                app.MapGet("/api/components/manufacturers", GetAll).AllowAnonymous();

                var admin = app.MapGroup("/api/components/manufacturers").RequireAuthorization(Policies.Admin);
                admin.MapPost("", Create).WithValidation<ManufacturerInput>();
                admin.MapPut("{id:int}", Rename).WithValidation<ManufacturerInput>();
                admin.MapDelete("{id:int}", Delete);
            }
        }
    }
}
