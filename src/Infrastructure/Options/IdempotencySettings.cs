namespace Infrastructure.Options;

public sealed class IdempotencySettings
{
    public const string SectionName = "Idempotency";

    /// <summary>Master switch. When false, X-Idempotency-Key headers are ignored.</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// How long an idempotency key + its cached response is retained (minutes).
    /// Requests with the same key within this window return the original response.
    /// </summary>
    public int ExpirationMinutes { get; init; } = 60;

    /// <summary>HTTP methods to enforce idempotency on. GET/HEAD are idempotent by nature.</summary>
    public string[] ApplyToMethods { get; init; } = ["POST", "PUT", "PATCH"];
}
