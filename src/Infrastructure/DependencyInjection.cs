using Application.Services;
using Domain.Users;
using Fido2NetLib;
using Infrastructure.Jobs;
using Infrastructure.Options;
using Infrastructure.Persistance;
using Infrastructure.Persistance.Repositories;
using Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Quartz;
using Quartz.Impl.Matchers;
using SharedKernel;
using System.Security.Claims;
using System.Text;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // SQL Database Configuration
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        // Identity Configuration
        var passwordPolicy = configuration
            .GetSection(PasswordPolicySettings.SectionName)
            .Get<PasswordPolicySettings>() ?? new PasswordPolicySettings();

        services.AddIdentity<User, IdentityRole>(options =>
        {
            options.SignIn.RequireConfirmedEmail = true;

            options.Password.RequiredLength = passwordPolicy.RequiredLength;
            options.Password.RequireNonAlphanumeric = passwordPolicy.RequireNonAlphanumeric;
            options.Password.RequireDigit = passwordPolicy.RequireDigit;
            options.Password.RequireUppercase = passwordPolicy.RequireUppercase;
            options.Password.RequireLowercase = passwordPolicy.RequireLowercase;
            options.Password.RequiredUniqueChars = passwordPolicy.RequiredUniqueChars;

            options.User.RequireUniqueEmail = true;
            options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_";

            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.AllowedForNewUsers = true;

            options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddRoles<IdentityRole>()
            .AddDefaultTokenProviders();

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
            .AddJwtBearer(options =>
            {
                // Keep original JWT claim names (sub, sid, role, …) instead of remapping
                // them to WS-Federation URIs. Without this, "sid" → ClaimTypes.Sid (full URI),
                // breaking User.FindFirstValue("sid") and making GetSessionId() always null.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidIssuer = configuration["Jwt:Issuer"],
                    ValidAudience = configuration["Jwt:Audience"],
                    ClockSkew = TimeSpan.Zero,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:SecretKey"]!)),
                    // MapInboundClaims = false keeps "role" as-is; tell the authorization
                    // middleware to use "role" as the role claim type so [Authorize(Roles=...)] works.
                    RoleClaimType = "role",
                    NameClaimType = "unique_name"
                };
                // Validate the session is still active on every authenticated request.
                // This makes startup revocation (and admin session revocation) take effect
                // immediately rather than waiting for the access token to expire.
                options.Events = new JwtBearerEvents
                {
                    // SignalR WebSocket connections cannot send custom headers from browsers,
                    // so the JWT is passed as ?access_token= in the query string instead.
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(accessToken) &&
                            context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        {
                            context.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = async context =>
                    {
                        var sid = context.Principal?.FindFirstValue("sid");
                        if (sid is null || !Guid.TryParse(sid, out var sessionId))
                        {
                            context.Fail("Missing or invalid session claim.");
                            return;
                        }

                        var db = context.HttpContext.RequestServices
                            .GetRequiredService<ApplicationDbContext>();

                        var isActive = await db.RefreshTokens
                            .AnyAsync(t => t.Id == sessionId
                                       && t.RevokedAtUtc == null
                                       && t.ExpiresUtc > DateTime.UtcNow);

                        if (!isActive)
                            context.Fail("Session has been revoked or expired.");
                    }
                };
            });

        // Fido2NetLib — handles WebAuthn ceremonies with server-side challenge storage (IMemoryCache).
        // This avoids the cross-origin cookie problem of Identity's built-in passkey methods.
        services.AddMemoryCache();

        // Distributed Cache — Redis when Redis:Enabled = true, otherwise in-process memory.
        // IDistributedCache is available for injection anywhere caching is needed.
        var redisSettings = configuration
            .GetSection(RedisSettings.SectionName)
            .Get<RedisSettings>() ?? new RedisSettings();

        if (redisSettings.Enabled)
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisSettings.ConnectionString;
                options.InstanceName = redisSettings.InstanceName;
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }
        services.AddFido2(options =>
        {
            options.ServerDomain = configuration["Fido2:ServerDomain"] ?? "localhost";
            options.ServerName = configuration["App:Name"] ?? "App";
            options.Origins = configuration.GetSection("Fido2:Origins").Get<HashSet<string>>() ?? [];
            options.TimestampDriftTolerance = 300000;
        });

        // Email + App + S3 + Policy + Redis Options
        services.Configure<SmtpSettings>(configuration.GetSection(SmtpSettings.SectionName));
        services.Configure<AppSettings>(configuration.GetSection(AppSettings.SectionName));
        services.Configure<S3Settings>(configuration.GetSection(S3Settings.SectionName));
        services.Configure<PasswordPolicySettings>(configuration.GetSection(PasswordPolicySettings.SectionName));
        services.Configure<RequestLoggingSettings>(configuration.GetSection(RequestLoggingSettings.SectionName));
        services.Configure<RedisSettings>(configuration.GetSection(RedisSettings.SectionName));

        // Core infrastructure
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ApplicationDbContext>());

        // Profile image storage — swap implementation via App:ProfileImageStorage
        var profileImageStorage = configuration["App:ProfileImageStorage"] ?? "Database";
        if (profileImageStorage.Equals("S3", StringComparison.OrdinalIgnoreCase))
            services.AddScoped<IProfileImageStore, S3ProfileImageStore>();
        else
            services.AddScoped<IProfileImageStore, DatabaseProfileImageStore>();

        // Repository Registration
        services.AddScoped<IAppCountryRepository, AppCountryRepository>();
        services.AddScoped<ITokenRepository, TokenRepository>();

        // Service Registration
        // TryAdd — defers to HubRealtimeNotifier when the Api project has already registered it;
        // falls back to no-op in test hosts and background workers.
        services.TryAddScoped<IRealtimeNotifier, NoOpRealtimeNotifier>();
        services.AddScoped<IAppCountryService, AppCountryService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserProfileService, UserProfileService>();
        services.AddScoped<IPasskeyService, PasskeyService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IAuditLogService, AuditLogService>();

        // EmailSender implements both IEmailSender (low-level transport)
        // and IEmailSender<User> (Identity plumbing for built-in endpoints)
        services.AddScoped<EmailSender>();
        services.AddScoped<IEmailSender>(sp => sp.GetRequiredService<EmailSender>());
        services.AddScoped<Microsoft.AspNetCore.Identity.IEmailSender<User>>(sp => sp.GetRequiredService<EmailSender>());

        services.AddScoped<IAppEmailSender, AppEmailSender>();

        // Quartz background jobs
        services.Configure<QuartzSettings>(configuration.GetSection(QuartzSettings.SectionName));

        // Job history cache (singleton — survives across requests, cleared on app restart)
        services.AddSingleton<IJobHistoryCache, JobHistoryCache>();
        services.AddSingleton<JobAuditListener>();

        var tokenCron = configuration["Quartz:TokenCleanup:CronSchedule"] ?? "0 0 3 * * ?";
        var auditCron = configuration["Quartz:AuditLogCleanup:CronSchedule"] ?? "0 0 4 * * ?";

        services.AddQuartz(q =>
        {
            // Global listener — fires after every job execution (scheduled or manual)
            q.AddJobListener<JobAuditListener>(GroupMatcher<JobKey>.AnyGroup());

            q.AddJob<ExpiredTokenCleanupJob>(j => j
                .WithIdentity("ExpiredTokenCleanupJob")
                .WithDescription("Deletes expired and revoked refresh tokens past the retention window."));

            q.AddTrigger(t => t
                .ForJob("ExpiredTokenCleanupJob")
                .WithIdentity("ExpiredTokenCleanupTrigger")
                .WithCronSchedule(tokenCron));

            q.AddJob<ExpiredAuditLogArchiveJob>(j => j
                .WithIdentity("ExpiredAuditLogArchiveJob")
                .WithDescription("Purges audit log records older than the retention window."));

            q.AddTrigger(t => t
                .ForJob("ExpiredAuditLogArchiveJob")
                .WithIdentity("ExpiredAuditLogArchiveTrigger")
                .WithCronSchedule(auditCron));
        });

        // Gracefully waits for running jobs to finish before the host shuts down.
        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        return services;
    }
}
