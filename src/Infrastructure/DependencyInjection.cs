using Application.Services;
using Domain.Users;
using Infrastructure.Options;
using Infrastructure.Persistance;
using Infrastructure.Persistance.Repositories;
using Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using SharedKernel;
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
        services.AddIdentity<User, IdentityRole>(options =>
        {
            options.SignIn.RequireConfirmedEmail = true;

            options.Password.RequiredLength = 8;
            options.Password.RequireNonAlphanumeric = true;
            options.Password.RequireDigit = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireLowercase = true;
            options.Password.RequiredUniqueChars = 1;

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
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidIssuer = configuration["Jwt:Issuer"],
                    ValidAudience = configuration["Jwt:Audience"],
                    ClockSkew = TimeSpan.Zero,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:SecretKey"]!))
                };
            });

        // Passkey Options (ServerDomain defaults to Host header if not set)
        services.Configure<IdentityPasskeyOptions>(options =>
        {
            var serverDomain = configuration["Passkeys:ServerDomain"];
            if (!string.IsNullOrEmpty(serverDomain))
                options.ServerDomain = serverDomain;
        });

        // Email + App + S3 Options
        services.Configure<SmtpSettings>(configuration.GetSection(SmtpSettings.SectionName));
        services.Configure<AppSettings>(configuration.GetSection(AppSettings.SectionName));
        services.Configure<S3Settings>(configuration.GetSection(S3Settings.SectionName));

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
        services.AddScoped<IAppCountryService, AppCountryService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserProfileService, UserProfileService>();
        services.AddScoped<IPasskeyService, PasskeyService>();

        // EmailSender implements both IEmailSender (low-level transport)
        // and IEmailSender<User> (Identity plumbing for built-in endpoints)
        services.AddScoped<EmailSender>();
        services.AddScoped<IEmailSender>(sp => sp.GetRequiredService<EmailSender>());
        services.AddScoped<Microsoft.AspNetCore.Identity.IEmailSender<User>>(sp => sp.GetRequiredService<EmailSender>());

        services.AddScoped<IAppEmailSender, AppEmailSender>();

        return services;
    }
}
