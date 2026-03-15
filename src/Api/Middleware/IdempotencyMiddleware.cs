using Infrastructure.Options;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace Api.Middleware;

/// <summary>
/// Enforces idempotency for mutating HTTP methods (POST, PUT, PATCH by default).
///
/// How it works:
///   1. Client sends X-Idempotency-Key: {unique-uuid} with the request.
///   2. Middleware checks IDistributedCache for key "{method}:{path}:{idempotency-key}".
///   3. Cache hit  → return the stored response immediately (status + body);
///                   X-Idempotent-Replayed: true is added to the response.
///   4. Cache miss → let the request proceed normally; if the response is 2xx,
///                   store the response in cache for ExpirationMinutes.
///
/// Only 2xx responses are cached — errors are never replayed so the client
/// can retry after fixing the request.
///
/// Relies on IDistributedCache (in-memory or Redis — automatically wired by
/// DependencyInjection.cs based on Redis:Enabled).
/// </summary>
public sealed class IdempotencyMiddleware
{
    public const string HeaderName = "X-Idempotency-Key";
    private const string ReplayedHeader = "X-Idempotent-Replayed";

    private readonly RequestDelegate _next;
    private readonly ILogger<IdempotencyMiddleware> _logger;
    private readonly IdempotencySettings _settings;
    private readonly IDistributedCache _cache;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public IdempotencyMiddleware(
        RequestDelegate next,
        ILogger<IdempotencyMiddleware> logger,
        IOptions<IdempotencySettings> options,
        IDistributedCache cache)
    {
        _next = next;
        _logger = logger;
        _settings = options.Value;
        _cache = cache;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_settings.Enabled
            || !_settings.ApplyToMethods.Contains(context.Request.Method, StringComparer.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var idempotencyKey = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            await _next(context);
            return;
        }

        var cacheKey = $"idempotency:{context.Request.Method}:{context.Request.Path}:{idempotencyKey}";

        // ── Cache hit → replay stored response ───────────────────────────────
        var cached = await _cache.GetStringAsync(cacheKey);
        if (cached is not null)
        {
            var entry = JsonSerializer.Deserialize<IdempotencyEntry>(cached, JsonOpts);
            if (entry is not null)
            {
                _logger.LogDebug("Idempotency replay for key {Key} on {Method} {Path}",
                    idempotencyKey, context.Request.Method, context.Request.Path);

                context.Response.StatusCode = entry.StatusCode;
                context.Response.ContentType = entry.ContentType;
                context.Response.Headers[ReplayedHeader] = "true";
                await context.Response.WriteAsync(entry.Body, Encoding.UTF8);
                return;
            }
        }

        // ── Cache miss → execute and conditionally cache ─────────────────────
        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);
        }
        finally
        {
            buffer.Position = 0;
            var responseBody = await new StreamReader(buffer, Encoding.UTF8, leaveOpen: true).ReadToEndAsync();
            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody);
            context.Response.Body = originalBody;

            // Only cache successful responses — never cache errors
            if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300)
            {
                var entry = new IdempotencyEntry(
                    context.Response.StatusCode,
                    context.Response.ContentType ?? "application/json",
                    responseBody);

                var serialized = JsonSerializer.Serialize(entry, JsonOpts);
                await _cache.SetStringAsync(cacheKey, serialized, new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_settings.ExpirationMinutes)
                });
            }
        }
    }

    private sealed record IdempotencyEntry(int StatusCode, string ContentType, string Body);
}
