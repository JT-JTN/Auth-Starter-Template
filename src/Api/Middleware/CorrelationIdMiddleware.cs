using Serilog.Context;

namespace Api.Middleware;

/// <summary>
/// Reads X-Correlation-ID from the request header (or generates a new one) and:
///   - Echoes it back on the response header so callers can trace their requests.
///   - Stores it in HttpContext.Items for use by other middleware and controllers.
///   - Pushes it into Serilog's LogContext so every log line for this request
///     includes a CorrelationId property.
/// Must be the first middleware in the pipeline so the correlation ID is available
/// to all downstream components including exception handling and request logging.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemsKey = "CorrelationId";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N");

        context.Items[ItemsKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
