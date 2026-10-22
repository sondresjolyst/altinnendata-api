using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using SjolystInnovation.AppKit.Auth;

namespace altinnendata_api.Features.Contact
{
    /// <summary>
    /// The admin's enquiries: list, open, take over, change status and delete.
    /// An owner who is no longer an active admin reads as none with <c>OwnerRemoved</c>, before the daily job clears them.
    /// </summary>
    public static class AdminEnquiries
    {
        public const int PageSize = 50;

        /// <summary>An enquiry with its owner as stored and whether that owner is still an active admin.</summary>
        private record Row(Enquiry Enquiry, bool OwnerActive, string? OwnerFirstName, string? OwnerLastName);

        private static IQueryable<Row> Rows(ApplicationDbContext db, IQueryable<Enquiry> enquiries)
        {
            var active = EnquiryOwners.ActiveAdminIds(db);
            return enquiries.Select(e => new Row(
                e,
                e.AssignedUserId != null && active.Contains(e.AssignedUserId),
                e.AssignedUser != null ? e.AssignedUser.FirstName : null,
                e.AssignedUser != null ? e.AssignedUser.LastName : null));
        }

        private static string DisplayName(string? first, string? last) => $"{first} {last}".Trim();

        private static (AssigneeDto? Owner, bool Removed) EffectiveOwner(Row row) =>
            row.OwnerActive
                ? (new AssigneeDto(row.Enquiry.AssignedUserId!, DisplayName(row.OwnerFirstName, row.OwnerLastName)), false)
                : (null, row.Enquiry.AssignedUserId != null || row.Enquiry.OwnerRemoved);

        private static EnquirySummaryDto Summary(Row row)
        {
            var (owner, removed) = EffectiveOwner(row);
            var e = row.Enquiry;
            return new EnquirySummaryDto(e.Id, e.CreatedAt, e.Name, e.BuildTitle, e.UseCase, e.Status, owner, removed);
        }

        private static EnquiryDto Detail(Row row)
        {
            var (owner, removed) = EffectiveOwner(row);
            var e = row.Enquiry;
            return new EnquiryDto(e.Id, e.CreatedAt, e.Name, e.BuildTitle, e.UseCase, e.Status, owner, removed,
                e.Email, e.Phone, e.BudgetNok, e.BuildSlug, e.Locale, e.Message, e.ReceiptSent, e.NoticeSentAt, e.UpdatedAt);
        }

        private static async Task<EnquiryDto?> LoadAsync(ApplicationDbContext db, int id, CancellationToken ct)
        {
            var row = await Rows(db, db.Enquiries.AsNoTracking().Where(e => e.Id == id)).FirstOrDefaultAsync(ct);
            return row == null ? null : Detail(row);
        }

        public static async Task<IResult> List(
            ApplicationDbContext db,
            HttpContext http,
            [FromQuery] EnquiryStatus[]? status,
            bool mine,
            string? q,
            int? page,
            CancellationToken ct)
        {
            var enquiries = db.Enquiries.AsNoTracking();
            enquiries = status is { Length: > 0 }
                ? enquiries.Where(e => status.Contains(e.Status))
                : enquiries.Where(e => e.Status != EnquiryStatus.Done);

            if (mine)
            {
                var me = http.User.GetUserId();
                enquiries = enquiries.Where(e => e.AssignedUserId == me);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                enquiries = enquiries.Where(e => e.Name.ToLower().Contains(term) || e.Email.ToLower().Contains(term));
            }

            var total = await enquiries.CountAsync(ct);
            var rows = await Rows(db, enquiries
                    .OrderByDescending(e => e.CreatedAt)
                    .ThenByDescending(e => e.Id)
                    .Skip((Math.Max(page ?? 1, 1) - 1) * PageSize)
                    .Take(PageSize))
                .ToListAsync(ct);

            return TypedResults.Ok(new EnquiryPage(rows.Select(Summary).ToList(), total));
        }

        public static async Task<IResult> Count(ApplicationDbContext db, CancellationToken ct) =>
            TypedResults.Ok(new EnquiryCountDto(await db.Enquiries.CountAsync(e => e.Status == EnquiryStatus.New, ct)));

        public static async Task<IResult> Assignees(ApplicationDbContext db, CancellationToken ct)
        {
            var active = EnquiryOwners.ActiveAdminIds(db);
            var users = await db.Users.AsNoTracking()
                .Where(u => active.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName })
                .ToListAsync(ct);

            return TypedResults.Ok(users
                .Select(u => new AssigneeDto(u.Id, DisplayName(u.FirstName, u.LastName)))
                .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList());
        }

        public static async Task<IResult> Get(int id, ApplicationDbContext db, CancellationToken ct)
        {
            var dto = await LoadAsync(db, id, ct);
            return dto == null ? TypedResults.NotFound() : TypedResults.Ok(dto);
        }

        /// <summary>
        /// Changes the status and, when asked, the owner. The owner check and the write are one conditional update,
        /// so two admins taking the same case cannot both win.
        /// </summary>
        public static async Task<IResult> Patch(int id, UpdateEnquiryRequest request, ApplicationDbContext db, CancellationToken ct)
        {
            var active = EnquiryOwners.ActiveAdminIds(db);
            if (request.SetOwner && request.AssignedUserId != null && !await active.AnyAsync(a => a == request.AssignedUserId, ct))
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.AssignedUserId)] = ["The owner must be an active admin."],
                });

            var now = DateTime.UtcNow;
            var expected = request.ExpectedAssignedUserId;
            var target = db.Enquiries.Where(e => e.Id == id);

            if (request.SetOwner)
            {
                // The expected owner is the effective one, so an owner who is no longer an active admin counts as none.
                target = expected == null
                    ? target.Where(e => e.AssignedUserId == null || !active.Contains(e.AssignedUserId))
                    : target.Where(e => e.AssignedUserId == expected && active.Contains(expected));
            }

            var changed = await target.ExecuteUpdateAsync(s =>
            {
                s.SetProperty(e => e.UpdatedAt, now);
                if (request.Status is { } status) s.SetProperty(e => e.Status, status);
                if (request.SetOwner)
                {
                    s.SetProperty(e => e.AssignedUserId, request.AssignedUserId);
                    s.SetProperty(e => e.OwnerRemoved, false);
                }
            }, ct);

            var current = await LoadAsync(db, id, ct);
            if (current == null) return TypedResults.NotFound();
            return changed == 0 ? TypedResults.Conflict(current) : TypedResults.Ok(current);
        }

        public static async Task<IResult> Delete(int id, ApplicationDbContext db, CancellationToken ct) =>
            await db.Enquiries.Where(e => e.Id == id).ExecuteDeleteAsync(ct) == 0
                ? TypedResults.NotFound()
                : TypedResults.NoContent();

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app)
            {
                var admin = app.MapGroup("/api/admin/enquiries").RequireAuthorization(Policies.Admin);
                admin.MapGet("", List);
                admin.MapGet("count", Count);
                admin.MapGet("assignees", Assignees);
                admin.MapGet("{id:int}", Get);
                admin.MapPatch("{id:int}", Patch).WithValidation<UpdateEnquiryRequest>();
                admin.MapDelete("{id:int}", Delete);
            }
        }
    }
}
