using Infrastructure.Options;
using Microsoft.Extensions.Options;
using System.Net;

namespace Api.Middleware;

/// <summary>
/// Blocks or allows requests based on the caller's IP address.
///
/// Mode "allowlist": only IPs that match an entry are permitted; all others → 403.
/// Mode "blocklist": IPs that match an entry are denied → 403; all others pass.
///
/// Each entry may be an exact IP ("1.2.3.4") or a CIDR range ("10.0.0.0/8").
/// Both IPv4 and IPv6 are supported. IPv4-mapped IPv6 addresses (::ffff:x.x.x.x)
/// are unwrapped to their IPv4 form before matching.
///
/// Register before rate limiting and auth so blocked IPs never hit those layers.
/// </summary>
public sealed class IpFilteringMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<IpFilteringMiddleware> _logger;
    private readonly IpFilteringSettings _settings;

    public IpFilteringMiddleware(
        RequestDelegate next,
        ILogger<IpFilteringMiddleware> logger,
        IOptions<IpFilteringSettings> options)
    {
        _next = next;
        _logger = logger;
        _settings = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_settings.Enabled)
        {
            await _next(context);
            return;
        }

        var remoteIp = GetRemoteIp(context);
        if (remoteIp is null)
        {
            // Cannot determine caller IP — fail closed in allowlist mode, pass in blocklist
            if (_settings.Mode.Equals("allowlist", StringComparison.OrdinalIgnoreCase))
            {
                await BlockAsync(context, "unknown");
                return;
            }
            await _next(context);
            return;
        }

        var isAllowlist = _settings.Mode.Equals("allowlist", StringComparison.OrdinalIgnoreCase);
        var list = isAllowlist ? _settings.Allowlist : _settings.Blocklist;
        var matches = MatchesAny(remoteIp, list);

        if (isAllowlist && !matches)
        {
            await BlockAsync(context, remoteIp.ToString());
            return;
        }

        if (!isAllowlist && matches)
        {
            await BlockAsync(context, remoteIp.ToString());
            return;
        }

        await _next(context);
    }

    private async Task BlockAsync(HttpContext context, string ip)
    {
        _logger.LogWarning("IP filtering blocked request from {IpAddress} ({Path})", ip, context.Request.Path);
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://httpstatuses.io/403",
            title = "Forbidden",
            status = 403,
            detail = "Your IP address is not permitted to access this resource.",
            instance = context.Request.Path.ToString()
        });
    }

    private static IPAddress? GetRemoteIp(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;
        // Unwrap IPv4-mapped IPv6 addresses (::ffff:x.x.x.x → x.x.x.x)
        if (ip is not null && ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();
        return ip;
    }

    private static bool MatchesAny(IPAddress address, string[] entries)
    {
        foreach (var entry in entries)
        {
            if (entry.Contains('/'))
            {
                if (IsInCidr(address, entry)) return true;
            }
            else
            {
                if (IPAddress.TryParse(entry, out var parsed) && address.Equals(parsed))
                    return true;
            }
        }
        return false;
    }

    private static bool IsInCidr(IPAddress address, string cidr)
    {
        var slash = cidr.IndexOf('/');
        if (!IPAddress.TryParse(cidr[..slash], out var network)) return false;
        if (!int.TryParse(cidr[(slash + 1)..], out var prefixLen)) return false;

        var addrBytes = address.GetAddressBytes();
        var netBytes = network.GetAddressBytes();

        // Address families must match
        if (addrBytes.Length != netBytes.Length) return false;

        var fullBytes = prefixLen / 8;
        var remainder = prefixLen % 8;

        for (var i = 0; i < fullBytes; i++)
            if (addrBytes[i] != netBytes[i]) return false;

        if (remainder > 0)
        {
            var mask = (byte)(0xFF << (8 - remainder));
            if ((addrBytes[fullBytes] & mask) != (netBytes[fullBytes] & mask)) return false;
        }

        return true;
    }
}
