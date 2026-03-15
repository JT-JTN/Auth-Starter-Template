namespace Infrastructure.Options;

public sealed class RequestLoggingSettings
{
    public const string SectionName = "RequestLogging";

    /// <summary>Master switch. When false, middleware is a no-op pass-through.</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>Whether to capture and log the request body.</summary>
    public bool LogRequestBody { get; init; } = true;

    /// <summary>Whether to capture and log the response body.</summary>
    public bool LogResponseBody { get; init; } = true;

    /// <summary>Max characters of body to log, preventing log flooding on large payloads.</summary>
    public int MaxBodyLength { get; init; } = 4096;

    /// <summary>
    /// Path prefixes to skip entirely (e.g. health checks, SignalR hubs).
    /// Matched case-insensitively.
    /// </summary>
    public string[] ExcludePaths { get; init; } = ["/health", "/hubs"];

    /// <summary>
    /// Top-level JSON property names whose values are replaced with "[REDACTED]".
    /// Case-insensitive. Applied to both request and response bodies.
    /// </summary>
    public string[] SensitiveFields { get; init; } =
    [
        "password", "currentPassword", "newPassword", "confirmPassword",
        "confirmNewPassword", "token", "refreshToken", "accessToken", "secretKey"
    ];
}
