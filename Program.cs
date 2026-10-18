using FluentValidation;
using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using altinnendata_api.Constants;
using altinnendata_api.Features.Admin;
using altinnendata_api.Features.Shop;
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
using SjolystInnovation.AppKit.Storage.Images;
using SjolystInnovation.AppKit.AdminStats;

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

            builder.Services.AddMemoryCache();
            builder.Services.AddAppKitIdentity<User, ApplicationDbContext>();

            builder.Services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
                });

            builder.Services.AddAppKitBrevo(builder.Configuration.GetSection("BrevoSettings"));
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
            builder.Services.AddAppKitContentImages<ApplicationDbContext>(builder.Configuration.GetSection("Storage"));
            builder.Services.AddHttpClient("finn")
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
            builder.Services.AddAppKitDailyStats<ApplicationDbContext, DailyStatSnapshot>(o => o.LiveSampler = AdminDashboard.SampleAsync);

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

            app.Logger.LogInformation("Starting API.");
            await app.Services.MigrateAppKitDatabaseAsync<ApplicationDbContext>(app.Configuration);
            await app.Services.SeedAppKitRolesAsync(RoleNames.AllRoles);

            using (var scope = app.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                if (await context.AppSettings.FindAsync(1) == null)
                {
                    context.AppSettings.Add(new AppSettings());
                    await context.SaveChangesAsync();
                }

                await SeedData.EnsureComponentCategoriesAsync(context);
                await ShopSeed.EnsureAsync(context, CancellationToken.None);
                await app.Services.SeedAppKitLegalPagesAsync<ApplicationDbContext>(LegalSeedText.All);
                await app.Services.SeedAppKitFirstAdminAsync<User>(app.Configuration, u => new User { FirstName = u.FirstName, LastName = u.LastName });
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
            app.UseAppKitBadRequests();
            app.MapControllers();
            app.MapEndpoints();
            app.MapAppKitAuth<User, ApplicationDbContext>(configure: o => o.IsActive = u => !u.IsDeleted);
            app.MapAppKitAccount<User>();
            app.MapAppKitSiteSettings<ApplicationDbContext, AppSettings>(configure: o => o.Policy = Policies.Admin);
            app.MapAppKitLegalPages<ApplicationDbContext>(configure: o =>
            {
                o.Locales = Locales.Supported;
                o.Policy = Policies.Admin;
            });
            app.MapAppKitPageSections<ApplicationDbContext>(configure: o =>
            {
                o.Locales = Locales.Supported;
                o.DefaultLocale = Locales.Default;
                o.Policy = Policies.Admin;
            });
            app.MapAppKitUserAdmin<User, ApplicationDbContext>(configure: o =>
            {
                o.Roles = RoleNames.AllRoles;
                o.Policy = Policies.Admin;
                o.NewUser = u => new User { FirstName = u.FirstName, LastName = u.LastName };
            });
            app.MapAppKitContentImages<ApplicationDbContext>("/api/content-images", o => o.Policy = Policies.Admin);
            app.MapAppKitEmailStats(configure: o => o.Policy = Policies.Admin);
            app.MapAppKitHealthChecks();
            app.Run();
        }
    }
}
