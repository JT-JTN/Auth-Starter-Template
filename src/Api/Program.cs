using Api.Hubs;
using Api.Middleware;
using Api.Services;
using Api.Swagger;
using Application.Services;
using Asp.Versioning;
using Infrastructure;
using Infrastructure.Persistance;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Serilog;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Text;
using System.Threading.RateLimiting;

// Ensure the logs directory exists before Serilog tries to write to it
Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "logs"));

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, services, config) => config
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("CorrelationId", "")   // default; overridden per-request by CorrelationIdMiddleware
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{CorrelationId}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/api-.log",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{CorrelationId}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

// Fail fast: validate required secrets before anything starts
var jwtSecret = builder.Configuration["Jwt:SecretKey"];
if (string.IsNullOrEmpty(jwtSecret) || Encoding.UTF8.GetByteCount(jwtSecret) < 32)
    throw new InvalidOperationException(
        "Jwt:SecretKey must be at least 32 bytes (256 bits). " +
        "Set it via User Secrets: dotnet user-secrets set \"Jwt:SecretKey\" \"<your-key>\" --project src/Api");

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
})
.AddApiExplorer(options =>
{
    // GroupNameFormat "'v'VVV" → "v1", "v1.1", etc.
    options.GroupNameFormat = "'v'VVV";
    // Replaces {version:apiVersion} in route templates with the version string in Swagger docs.
    options.SubstituteApiVersionInUrl = true;
});

// ConfigureSwaggerOptions generates one SwaggerDoc per discovered API version.
builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();

builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT access token (without the Bearer prefix)."
    });

    options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer"),
            new List<string>()
        }
    });
});

builder.Services.AddHttpContextAccessor();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowWebClient", policy =>
    {
        policy.WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [])
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", config =>
    {
        config.PermitLimit = 10;
        config.Window = TimeSpan.FromMinutes(1);
        config.QueueLimit = 0;
        config.AutoReplenishment = true;
    });

    options.AddFixedWindowLimiter("forgot-password", config =>
    {
        config.PermitLimit = 3;
        config.Window = TimeSpan.FromMinutes(1);
        config.QueueLimit = 0;
        config.AutoReplenishment = true;
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.Services.AddProblemDetails();

builder.Services.AddOutputCache(options =>
{
    // Countries list is public, static data — cache for 10 minutes with no vary-by.
    options.AddPolicy("countries", builder =>
        builder.Expire(TimeSpan.FromMinutes(10))
               .SetVaryByQuery([])
               .Tag("countries"));
});

builder.Services.AddLocalization();
builder.Services.AddSignalR();
builder.Services.AddScoped<IRealtimeNotifier, HubRealtimeNotifier>();

builder.Services.AddInfrastructureServices(builder.Configuration);

// Health checks — DB connectivity via EF Core ping
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>("database");

var app = builder.Build();

await Infrastructure.Persistance.DbInitializer.SeedRolesAsync(app.Services);

// Security: revoke all refresh tokens on startup so no session survives a restart.
// Skipped for InMemory databases (used in integration tests — ExecuteUpdateAsync unsupported).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
    {
        var revoked = await db.RefreshTokens
            .Where(t => t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, DateTime.UtcNow));
        if (revoked > 0)
            Log.Information("Startup: revoked {Count} active session(s).", revoked);
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        foreach (var description in app.DescribeApiVersions())
        {
            options.SwaggerEndpoint(
                $"/swagger/{description.GroupName}/swagger.json",
                $"Auth Starter API {description.GroupName.ToUpperInvariant()}");
        }
        options.DisplayRequestDuration();
    });
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<IpFilteringMiddleware>();

app.UseSerilogRequestLogging(opts =>
{
    opts.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    // Enrich Serilog request log with the correlation ID from the current request context.
    opts.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        if (httpContext.Items[CorrelationIdMiddleware.ItemsKey] is string cid)
            diagnosticContext.Set("CorrelationId", cid);
    };
});

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RequestResponseLoggingMiddleware>();

app.UseHttpsRedirection();

app.UseCors("AllowWebClient");

app.UseRequestLocalization(opts =>
{
    var supportedCultures = new[] { "en", "fr", "es" };
    opts.SetDefaultCulture("en")
        .AddSupportedCultures(supportedCultures)
        .AddSupportedUICultures(supportedCultures);
});

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.UseOutputCache();
app.UseMiddleware<IdempotencyMiddleware>();

app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");

// Health check endpoint — JSON response with component status
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds
            })
        });
        await context.Response.WriteAsync(result);
    }
});

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
