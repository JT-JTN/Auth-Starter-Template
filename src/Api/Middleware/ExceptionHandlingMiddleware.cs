using Domain.DomainExceptions;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace Api.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
        var correlationId = context.Items[CorrelationIdMiddleware.ItemsKey]?.ToString();
        var instance = context.Request.Path.ToString();

        context.Response.ContentType = "application/problem+json";

        switch (ex)
        {
            case ValidationException validation:
                _logger.LogWarning("Validation error: {Message}", ex.Message);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                var vpd = new ValidationProblemDetails(validation.Errors)
                {
                    Status = 400,
                    Title = "Validation failed",
                    Detail = ex.Message,
                    Instance = instance,
                    Type = "https://httpstatuses.io/400"
                };
                vpd.Extensions["traceId"] = traceId;
                vpd.Extensions["correlationId"] = correlationId;
                await context.Response.WriteAsJsonAsync(vpd);
                break;

            case AuthenticationException:
            case InvalidCredentialsException:
                _logger.LogWarning("Authentication error: {Message}", ex.Message);
                await WriteProblemAsync(context, 401, ex.Message, ex.Message, instance, traceId, correlationId);
                break;

            case EmailNotConfirmedException:
            case AuthorizationException:
                _logger.LogWarning("Authorization error: {Message}", ex.Message);
                await WriteProblemAsync(context, 403, ex.Message, ex.Message, instance, traceId, correlationId);
                break;

            case NotFoundException notFound:
                _logger.LogWarning("Not found: {Entity} ({Key})", notFound.EntityName, notFound.Key);
                await WriteProblemAsync(context, 404, ex.Message, ex.Message, instance, traceId, correlationId);
                break;

            case RateLimitExceededException rateLimit:
                _logger.LogWarning("Rate limit exceeded. RetryAfter: {RetryAfter}s", rateLimit.RetryAfter.TotalSeconds);
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = ((int)rateLimit.RetryAfter.TotalSeconds).ToString();
                var rlPd = BuildProblem(429, "Too many requests", ex.Message, instance, traceId, correlationId);
                rlPd.Extensions["retryAfterSeconds"] = (int)rateLimit.RetryAfter.TotalSeconds;
                await context.Response.WriteAsJsonAsync(rlPd);
                break;

            default:
                _logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
                await WriteProblemAsync(context, 500, "An unexpected error occurred", "An unexpected error occurred. Please try again later.", instance, traceId, correlationId);
                break;
        }
    }

    private static ProblemDetails BuildProblem(
        int status, string title, string detail, string instance,
        string? traceId, string? correlationId)
    {
        var pd = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = instance,
            Type = $"https://httpstatuses.io/{status}"
        };
        pd.Extensions["traceId"] = traceId;
        pd.Extensions["correlationId"] = correlationId;
        return pd;
    }

    private static async Task WriteProblemAsync(
        HttpContext context, int status, string title, string detail,
        string instance, string? traceId, string? correlationId)
    {
        context.Response.StatusCode = status;
        var pd = BuildProblem(status, title, detail, instance, traceId, correlationId);
        await context.Response.WriteAsJsonAsync(pd);
    }
}
