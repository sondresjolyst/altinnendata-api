using Microsoft.AspNetCore.Identity;
using altinnendata_api.Constants;
using altinnendata_api.Models;

namespace altinnendata_api.Infrastructure
{
    /// <summary>
    /// Creates the very first admin from configuration, because there is no public sign-up.
    /// Runs only while no admin exists — once one does, the configured values are ignored, so the
    /// password can be rotated or removed afterwards without changing anything.
    /// </summary>
    public static class AdminSeeder
    {
        public static async Task EnsureFirstAdminAsync(IServiceProvider services, IConfiguration config, ILogger logger)
        {
            var email = config["Seed:AdminEmail"];
            var password = config["Seed:AdminPassword"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                return;

            var users = services.GetRequiredService<UserManager<User>>();

            var admins = await users.GetUsersInRoleAsync(RoleNames.Admin);
            if (admins.Count > 0)
                return;

            var existing = await users.FindByEmailAsync(email);
            if (existing != null)
            {
                await users.AddToRoleAsync(existing, RoleNames.Admin);
                logger.LogInformation("Granted the Admin role to the existing seed user {Email}", email);
                return;
            }

            var user = new User
            {
                UserName = email,
                Email = email,
                FirstName = config["Seed:AdminFirstName"] ?? "Admin",
                LastName = config["Seed:AdminLastName"] ?? "",
                EmailConfirmed = true
            };

            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded)
            {
                logger.LogError("Could not create the seed admin: {Errors}",
                    string.Join("; ", created.Errors.Select(e => e.Description)));
                return;
            }

            await users.AddToRoleAsync(user, RoleNames.Admin);
            logger.LogInformation("Created the first admin {Email} from configuration", email);
        }
    }
}
