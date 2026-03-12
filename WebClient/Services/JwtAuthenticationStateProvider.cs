using Microsoft.AspNetCore.Components.Authorization;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;

namespace WebClient.Services;

/// <summary>
/// Custom AuthenticationStateProvider that parses JWT tokens stored in localStorage.
/// On startup, if the access token is expired but a refresh token exists, silently
/// refreshes before determining auth state — so users stay logged in across sessions.
/// </summary>
public sealed class JwtAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly TokenStorageService _tokenStorage;
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly AuthenticationState _anonymous =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    public JwtAuthenticationStateProvider(
        TokenStorageService tokenStorage,
        IHttpClientFactory httpClientFactory)
    {
        _tokenStorage = tokenStorage;
        _httpClientFactory = httpClientFactory;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var accessToken = await _tokenStorage.GetAccessTokenAsync();

        // Access token missing or expired — try silent refresh before giving up
        if (string.IsNullOrWhiteSpace(accessToken) || IsTokenExpired(accessToken))
        {
            accessToken = await TrySilentRefreshAsync();
            if (accessToken is null)
                return _anonymous;
        }

        var claims = ParseClaimsFromJwt(accessToken);
        // "role" is the short claim name written by JwtSecurityTokenHandler; specify it
        // as the roleClaimType so ClaimsPrincipal.IsInRole and [Authorize(Roles=...)] work.
        var identity = new ClaimsIdentity(claims, "jwt", nameType: "unique_name", roleType: "role");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    /// <summary>
    /// Call after login/register/refresh to push the new auth state to all subscribers.
    /// </summary>
    public void NotifyStateChanged() =>
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    // ── Silent refresh ────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to exchange the stored refresh token for a new token pair.
    /// Uses the public (unauthenticated) HTTP client to avoid circular handler dependency.
    /// Returns the new access token on success, null on failure.
    /// </summary>
    private async Task<string?> TrySilentRefreshAsync()
    {
        var refreshToken = await _tokenStorage.GetRefreshTokenAsync();
        if (string.IsNullOrWhiteSpace(refreshToken))
            return null;

        try
        {
            using var client = _httpClientFactory.CreateClient("public");
            var response = await client.PostAsJsonAsync("auth/refresh", refreshToken);

            if (!response.IsSuccessStatusCode)
            {
                await _tokenStorage.ClearAsync();
                return null;
            }

            var tokens = await response.Content.ReadFromJsonAsync<TokenDto>();
            if (tokens is null)
            {
                await _tokenStorage.ClearAsync();
                return null;
            }

            await _tokenStorage.SetTokensAsync(tokens.AccessToken, tokens.RefreshToken);
            return tokens.AccessToken;
        }
        catch
        {
            await _tokenStorage.ClearAsync();
            return null;
        }
    }

    // ── JWT Parsing ───────────────────────────────────────────────────────────

    private static bool IsTokenExpired(string jwt)
    {
        var claims = ParseClaimsFromJwt(jwt);
        var expClaim = claims.FirstOrDefault(c => c.Type == "exp")?.Value;
        if (!long.TryParse(expClaim, out var exp)) return true;
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= exp;
    }

    private static IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length != 3) return [];

            var payload = parts[1];
            var jsonBytes = Base64UrlDecode(payload);
            var kvp = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonBytes);
            if (kvp is null) return [];

            var claims = new List<Claim>();
            foreach (var (key, value) in kvp)
            {
                // Arrays (e.g. roles) become multiple claims
                if (value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in value.EnumerateArray())
                        claims.Add(new Claim(key, el.ToString()));
                }
                else
                {
                    claims.Add(new Claim(key, value.ToString()));
                }
            }
            return claims;
        }
        catch
        {
            return [];
        }
    }

    private static byte[] Base64UrlDecode(string base64Url)
    {
        var padded = base64Url
            .Replace('-', '+')
            .Replace('_', '/');

        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            _ => padded
        };

        return Convert.FromBase64String(padded);
    }
}
