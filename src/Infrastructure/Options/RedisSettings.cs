namespace Infrastructure.Options;

public sealed class RedisSettings
{
    public const string SectionName = "Redis";

    /// <summary>
    /// When false (default), the app uses an in-process MemoryCache for IDistributedCache.
    /// Set to true and provide ConnectionString to use Redis instead.
    /// </summary>
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// StackExchange.Redis connection string.
    /// Examples:
    ///   localhost:6379
    ///   redis-host:6379,password=secret,ssl=True,abortConnect=False
    /// </summary>
    public string ConnectionString { get; init; } = "localhost:6379";

    /// <summary>
    /// Prefix applied to every cache key, useful when multiple apps share the same Redis instance.
    /// </summary>
    public string InstanceName { get; init; } = "AuthApp:";

    /// <summary>Default sliding expiration for cache entries (minutes).</summary>
    public int DefaultExpirationMinutes { get; init; } = 60;
}
