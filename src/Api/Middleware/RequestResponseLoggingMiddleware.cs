using Infrastructure.Options;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json.Nodes;

namespace Api.Middleware;

/// <summary>
/// Logs HTTP request and response bodies for API endpoints.
///
/// Behaviour:
///   - Skipped entirely when RequestLogging:Enabled is false (default).
///   - Skipped for paths matching RequestLogging:ExcludePaths (health, hubs).
///   - Sensitive JSON fields (password, token, etc.) are replaced with "[REDACTED]".
///   - Bodies are truncated at RequestLogging:MaxBodyLength characters.
///   - Non-JSON bodies are logged as-is (no redaction attempted).
/// </summary>
public sealed class RequestResponseLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestResponseLoggingMiddleware> _logger;
    private readonly RequestLoggingSettings _settings;

    public RequestResponseLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestResponseLoggingMiddleware> logger,
        IOptions<RequestLoggingSettings> options)
    {
        _next = next;
        _logger = logger;
        _settings = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_settings.Enabled || IsExcluded(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // ── Request ──────────────────────────────────────────────────────────
        string? requestBody = null;
        if (_settings.LogRequestBody
            && context.Request.ContentLength > 0
            && IsJsonContent(context.Request.ContentType))
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            var raw = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
            requestBody = Redact(Truncate(raw));
        }

        _logger.LogInformation(
            "HTTP Request  {Method} {Path}{Query} | Body: {RequestBody}",
            context.Request.Method,
            context.Request.Path,
            context.Request.QueryString,
            requestBody ?? "(none)");

        // ── Response ─────────────────────────────────────────────────────────
        if (!_settings.LogResponseBody)
        {
            await _next(context);
            return;
        }

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
            var rawResponse = await new StreamReader(buffer, Encoding.UTF8, leaveOpen: true).ReadToEndAsync();
            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody);
            context.Response.Body = originalBody;

            var responseBody = IsJsonContent(context.Response.ContentType)
                ? Redact(Truncate(rawResponse))
                : Truncate(rawResponse);

            _logger.LogInformation(
                "HTTP Response {Method} {Path} | Status: {StatusCode} | Body: {ResponseBody}",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                string.IsNullOrWhiteSpace(responseBody) ? "(none)" : responseBody);
        }
    }

    private bool IsExcluded(PathString path)
    {
        foreach (var excluded in _settings.ExcludePaths)
        {
            if (path.StartsWithSegments(excluded, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsJsonContent(string? contentType)
        => contentType != null && contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase);

    private string Truncate(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return body;
        return body.Length > _settings.MaxBodyLength
            ? body[.._settings.MaxBodyLength] + $"... [truncated at {_settings.MaxBodyLength} chars]"
            : body;
    }

    private string Redact(string body)
    {
        if (string.IsNullOrWhiteSpace(body) || _settings.SensitiveFields.Length == 0)
            return body;

        try
        {
            var node = JsonNode.Parse(body);
            if (node is JsonObject obj)
            {
                RedactObject(obj);
                return obj.ToJsonString();
            }
        }
        catch
        {
            // Not valid JSON — return as-is
        }

        return body;
    }

    private void RedactObject(JsonObject obj)
    {
        foreach (var field in _settings.SensitiveFields)
        {
            var key = obj.Select(p => p.Key)
                .FirstOrDefault(k => string.Equals(k, field, StringComparison.OrdinalIgnoreCase));
            if (key is not null)
                obj[key] = "[REDACTED]";
        }

        foreach (var prop in obj.ToList())
        {
            if (prop.Value is JsonObject nested)
                RedactObject(nested);
        }
    }
}
