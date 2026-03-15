using Application.Services;
using Domain.Users;
using Infrastructure.Persistance;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Integration.Tests;

/// <summary>
/// Custom WebApplicationFactory that replaces the SQL Server DbContext with an
/// InMemory database and injects a valid JWT secret so the API can start up
/// without real user secrets or a real SQL Server instance.
/// </summary>
public class AuthWebApplicationFactory : WebApplicationFactory<Program>
{
    // Unique per factory instance — prevents InMemory database sharing between
    // different test classes that each get their own IClassFixture instance.
    private readonly string _dbName = Guid.NewGuid().ToString("N");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development"); // gives detailed errors in responses

        // UseSetting injects configuration into the host settings BEFORE Program.cs
        // reads builder.Configuration — this is required for fail-fast validation to pass.
        builder.UseSetting("Jwt:SecretKey", "integration-test-secret-key-that-is-long-enough-32+");
        builder.UseSetting("Jwt:Issuer", "TestIssuer");
        builder.UseSetting("Jwt:Audience", "TestAudience");
        builder.UseSetting("Jwt:ExpirationMinutes", "15");
        builder.UseSetting("Jwt:ExpirationDays", "7");
        builder.UseSetting("AllowedOrigins:0", "http://localhost:5000");
        builder.UseSetting("App:ProfileImageStorage", "Database");
        builder.UseSetting("App:ApiBaseUrl", "http://localhost");
        builder.UseSetting("Quartz:TokenCleanup:CronSchedule", "0 0 3 * * ?");
        builder.UseSetting("Quartz:TokenCleanup:RetentionDays", "30");
        builder.UseSetting("Quartz:AuditLogCleanup:CronSchedule", "0 0 4 * * ?");
        builder.UseSetting("Quartz:AuditLogCleanup:RetentionDays", "90");
        builder.UseSetting("Fido2:ServerDomain", "localhost");
        builder.UseSetting("Fido2:Origins:0", "https://localhost");
        builder.UseSetting("Smtp:Host", "localhost");
        builder.UseSetting("Smtp:Port", "25");
        builder.UseSetting("Smtp:UserName", "");
        builder.UseSetting("Smtp:Password", "");
        builder.UseSetting("Smtp:From", "noreply@test.local");

        builder.ConfigureServices(services =>
        {
            // Remove ALL EF Core registrations (options + context) to avoid
            // "multiple providers registered" error when adding InMemory on top of SQL Server.
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<ApplicationDbContext>();

            // Remove the EntityFrameworkCore.SqlServer service entries from the shared pool
            var descriptors = services
                .Where(d => d.ServiceType.FullName?.Contains("EntityFrameworkCore") == true
                         || d.ServiceType.FullName?.Contains("SqlServer") == true)
                .ToList();
            foreach (var d in descriptors)
                services.Remove(d);

            // Re-add with InMemory — unique name per factory instance avoids state leaking
            // across tests that use different factory instances.
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));

            // Replace the real email sender with a no-op so tests never hit SMTP
            services.RemoveAll<IAppEmailSender>();
            services.AddScoped<IAppEmailSender, NoOpEmailSender>();

            // Replace rate limiter policies with very high limits so tests never hit 429
            services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();
            services.AddRateLimiter(options =>
            {
                options.AddFixedWindowLimiter("auth", cfg =>
                {
                    cfg.PermitLimit = 10_000;
                    cfg.Window = TimeSpan.FromMinutes(1);
                    cfg.QueueLimit = 0;
                });
                options.AddFixedWindowLimiter("forgot-password", cfg =>
                {
                    cfg.PermitLimit = 10_000;
                    cfg.Window = TimeSpan.FromMinutes(1);
                    cfg.QueueLimit = 0;
                });
                options.RejectionStatusCode = 429;
            });
        });
    }

    /// <summary>
    /// Creates and seeds a confirmed user directly in the InMemory database.
    /// Returns the user's ID so tests can use it.
    /// </summary>
    public async Task<(User User, string PlainPassword)> SeedConfirmedUserAsync(
        string username = "seeduser",
        string email = "seed@example.com",
        string password = "SeedPass1!")
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        // Delete if exists from a previous test run in the same factory instance
        var existing = await userManager.FindByNameAsync(username);
        if (existing is not null)
            await userManager.DeleteAsync(existing);

        var user = new User
        {
            UserName = username,
            Email = email,
            FirstName = "Seed",
            LastName = "User",
            EmailConfirmed = true, // skip the email confirmation step
            CreatedAtUtc = DateTime.UtcNow
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Failed to seed test user: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        // Assign User role
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync("User"))
            await roleManager.CreateAsync(new IdentityRole("User"));
        await userManager.AddToRoleAsync(user, "User");

        return (user, password);
    }

    /// <summary>
    /// Creates and seeds a confirmed Admin user directly in the InMemory database.
    /// </summary>
    public async Task<(User User, string PlainPassword)> SeedAdminUserAsync(
        string username = "adminuser",
        string email = "admin@example.com",
        string password = "AdminPass1!")
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        var existing = await userManager.FindByNameAsync(username);
        if (existing is not null)
            await userManager.DeleteAsync(existing);

        var user = new User
        {
            UserName = username,
            Email = email,
            FirstName = "Admin",
            LastName = "User",
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Failed to seed admin user: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        foreach (var role in new[] { "User", "Admin" })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
            await userManager.AddToRoleAsync(user, role);
        }

        return (user, password);
    }
}

/// <summary>
/// No-op email sender used during integration tests so no SMTP is required.
/// </summary>
internal sealed class NoOpEmailSender : IAppEmailSender
{
    public Task SendEmailConfirmationAsync(string toEmail, string userName, string userId, string encodedToken)
        => Task.CompletedTask;

    public Task SendPasswordResetAsync(string toEmail, string userName, string encodedToken)
        => Task.CompletedTask;

    public Task SendAlertAsync(string toEmail, string subject, string title, string body)
        => Task.CompletedTask;

    public Task SendTemplatedEmailAsync(string toEmail, string subject, string templateName,
        Dictionary<string, string> variables)
        => Task.CompletedTask;
}
