using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Models;

namespace altinnendata_api.Features.Contact
{
    /// <summary>Who may own an enquiry.</summary>
    public static class EnquiryOwners
    {
        /// <summary>Ids of the users who are not deleted and hold the Admin role.</summary>
        public static IQueryable<string> ActiveAdminIds(ApplicationDbContext db) =>
            from user in db.Users
            join userRole in db.UserRoles on user.Id equals userRole.UserId
            join role in db.Roles on userRole.RoleId equals role.Id
            where !user.IsDeleted && role.Name == RoleNames.Admin
            select user.Id;
    }
}
