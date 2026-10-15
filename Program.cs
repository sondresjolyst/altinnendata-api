using FluentValidation;
using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Models.Admin;
using altinnendata_api.Profiles;
using altinnendata_api.Services;
using Serilog;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using SjolystInnovation.AppKit.Auth;
using SjolystInnovation.AppKit.SiteSettings;
using SjolystInnovation.AppKit.Hosting;
using SjolystInnovation.AppKit.Email;

namespace altinnendata_api
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .Enrich.FromLogContext()
                .ReadFrom.Configuration(builder.Configuration)
                .CreateLogger();

            builder.Host.UseSerilog();

            builder.Configuration.AddEnvironmentVariables();

            var mapsterConfig = TypeAdapterConfig.GlobalSettings;
            mapsterConfig.Scan(typeof(MappingProfile).Assembly);
            builder.Services.AddSingleton(mapsterConfig);
            builder.Services.AddScoped<IMapper, ServiceMapper>();

            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

            builder.Services.AddAppKitIdentity<User, ApplicationDbContext>();

            builder.Services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
                });

            builder.Services.AddAppKitBrevo(builder.Configuration.GetSection("BrevoSettings"));
            builder.Services.AddScoped<IEmailService, EmailService>();
            builder.Services.AddScoped<IEmailBrand, AuthEmailBrand>();
            builder.Services.AddAppKitAuthEmails<User>(o =>
            {
                o.Language = EmailLanguage.Norwegian;
                o.HeaderColor = "#0f3d3a";
                o.AccentColor = "#00887a";
                o.SetPasswordUrl = SiteLinks.SetPasswordPage(builder.Configuration);
            });
            builder.Services.AddAppKitAccounts<User, ApplicationDbContext>(o => o.AdminRole = RoleNames.Admin);
            builder.Services.AddAppKitAuth<ApplicationDbContext>(builder.Configuration.GetSection("Jwt"));
            builder.Services.AddSingleton<IImageStorageService, ImageStorageService>();
            builder.Services.AddHttpClient("finn")
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
            builder.Services.AddHostedService<StatsSnapshotService>();

            builder.Services.AddProblemDetails();
            builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
            builder.Services.AddValidatorsFromAssemblyContaining<Program>();
            builder.Services.AddEndpointsApiExplorer();

            builder.Services.AddAppKitJwtBearer<User>(u => !u.IsDeleted);

            builder.Services.AddAppKitHealthChecks<ApplicationDbContext>();

            builder.Services.AddAuthorization(options =>
            {
                options.AddPolicy("Admin", policy => policy.RequireRole(RoleNames.Admin));
            });

            var allowedOrigins = builder.Configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>() ?? [];

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAllOrigins", policy =>
                {
                    policy.WithOrigins(allowedOrigins)
                          .AllowAnyMethod()
                          .AllowAnyHeader()
                          .AllowCredentials();
                });
            });

            builder.Services.AddAppKitRateLimiting(builder.Configuration.GetSection("RateLimiting"));

            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Altinnendata API", Version = "v1" });
                if (File.Exists(xmlPath))
                    c.IncludeXmlComments(xmlPath);

                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    In = ParameterLocation.Header,
                    Description = "Please enter a valid token",
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    BearerFormat = "JWT",
                    Scheme = "Bearer"
                });

                c.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecuritySchemeReference("Bearer"),
                        new List<string>()
                    }
                });
            });

            var app = builder.Build();

            using (var scope = app.Services.CreateScope())
            {
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                logger.LogInformation("Starting API.");
                var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
                if (pending.Count > 0)
                {
                    logger.LogInformation("Applying {Count} pending migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
                    await context.Database.MigrateAsync();
                }

                foreach (var roleName in RoleNames.AllRoles)
                {
                    if (!await roleManager.RoleExistsAsync(roleName))
                        await roleManager.CreateAsync(new IdentityRole(roleName));
                }

                if (await context.AppSettings.FindAsync(1) == null)
                {
                    context.AppSettings.Add(new AppSettings());
                    await context.SaveChangesAsync();
                }

                await SeedData.EnsureComponentCategoriesAsync(context);
                await SeedData.EnsureLegalPagesAsync(context);
                await app.Services.SeedAppKitFirstAdminAsync<User>(app.Configuration, (email, first, last) => new User { FirstName = first, LastName = last }, RoleNames.Admin);
            }

            app.UseAppKitForwardedHeaders();

            app.UseExceptionHandler();

            app.UseAppKitSecurityHeaders();

            app.UseAppKitHttps();

            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Altinnendata API V1"));

            app.UseRouting();
            app.UseCors("AllowAllOrigins");
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseAppKitRateLimiting();
            app.MapControllers();
            app.MapEndpoints();
            app.MapAppKitAuthEndpoints<User, ApplicationDbContext>(configure: o => o.IsActive = u => !u.IsDeleted);
            app.MapAppKitAccount<User>();
            app.MapAppKitSiteSettings<ApplicationDbContext, AppSettings>(configure: o => o.Policy = Policies.Admin);
            app.MapAppKitUserAdmin<User, ApplicationDbContext>(configure: o =>
            {
                o.Roles = RoleNames.AllRoles;
                o.AdminRole = RoleNames.Admin;
                o.Policy = Policies.Admin;
                o.NewUser = (email, first, last) => new User { FirstName = first, LastName = last };
            });
            app.MapAppKitHealthChecks();
            app.Run();
        }
    }
}
