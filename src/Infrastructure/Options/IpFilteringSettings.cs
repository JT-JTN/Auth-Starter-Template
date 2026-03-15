namespace Infrastructure.Options;

public sealed class IpFilteringSettings
{
    public const string SectionName = "IpFiltering";

    /// <summary>Master switch. When false, all requests pass through.</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// "allowlist" — only IPs in Allowlist are permitted, all others are blocked (403).
    /// "blocklist" — IPs in Blocklist are denied (403), all others pass through.
    /// </summary>
    public string Mode { get; init; } = "blocklist";

    /// <summary>
    /// IPs or CIDR ranges to allow (used when Mode = "allowlist").
    /// Examples: "192.168.1.1", "10.0.0.0/8", "::1"
    /// </summary>
    public string[] Allowlist { get; init; } = [];

    /// <summary>
    /// IPs or CIDR ranges to block (used when Mode = "blocklist").
    /// Examples: "203.0.113.5", "198.51.100.0/24"
    /// </summary>
    public string[] Blocklist { get; init; } = [];
}
