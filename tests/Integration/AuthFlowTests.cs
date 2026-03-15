using Application.DTOs.Auth;
using FluentAssertions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// End-to-end integration tests for the auth flow.
/// Uses a real ASP.NET Core pipeline backed by an InMemory database.
/// </summary>
[Collection("Integration")]
public class AuthFlowTests
{
    private readonly AuthWebApplicationFactory _factory;
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public AuthFlowTests(AuthWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── Register ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_WithValidRequest_Returns200()
    {
        var dto = new RegisterDto(
            "Integration",
            "User",
            $"intuser_{Guid.NewGuid():N}",
            $"intuser_{Guid.NewGuid():N}@example.com",
            "IntTest1!",
            "IntTest1!");

        var response = await _client.PostAsJsonAsync("/api/auth/register", dto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Registration successful");
    }

    [Fact]
    public async Task Register_WithMismatchedPasswords_Returns400()
    {
        var dto = new RegisterDto(
            "Integration",
            "User",
            $"badpwd_{Guid.NewGuid():N}",
            $"badpwd_{Guid.NewGuid():N}@example.com",
            "Password1!",
            "Different1!");

        var response = await _client.PostAsJsonAsync("/api/auth/register", dto);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_Returns400()
    {
        // Use the factory's seeded user — email already taken
        await _factory.SeedConfirmedUserAsync(
            username: $"dup_{Guid.NewGuid():N}",
            email: "duplicate@example.com");

        var dto = new RegisterDto(
            "Dup",
            "User",
            $"dup2_{Guid.NewGuid():N}",
            "duplicate@example.com",
            "Password1!",
            "Password1!");

        var response = await _client.PostAsJsonAsync("/api/auth/register", dto);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Login ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_WithUnconfirmedEmail_Returns401()
    {
        // Register a user (email will NOT be confirmed because the NoOpEmailSender never confirms it
        // and we don't call the confirm-email endpoint)
        var username = $"unconfirmed_{Guid.NewGuid():N}";
        var email = $"{username}@example.com";
        var dto = new RegisterDto("Test", "User", username, email, "Password1!", "Password1!");
        await _client.PostAsJsonAsync("/api/auth/register", dto);

        // Try to log in before confirming email
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginDto(username, "Password1!"));

        loginResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await loginResponse.Content.ReadAsStringAsync();
        body.Should().Contain("confirmed");
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        var (user, _) = await _factory.SeedConfirmedUserAsync(
            username: $"wrongpwd_{Guid.NewGuid():N}",
            email: $"wrongpwd_{Guid.NewGuid():N}@example.com");

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginDto(user.UserName!, "WrongPassword!"));

        loginResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithConfirmedUser_Returns200AndTokens()
    {
        var (user, password) = await _factory.SeedConfirmedUserAsync(
            username: $"confirmed_{Guid.NewGuid():N}",
            email: $"confirmed_{Guid.NewGuid():N}@example.com");

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginDto(user.UserName!, password));

        var body500 = await loginResponse.Content.ReadAsStringAsync();
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK, body500);

        var tokenDto = await loginResponse.Content.ReadFromJsonAsync<TokenDto>(JsonOpts);
        tokenDto.Should().NotBeNull();
        tokenDto!.AccessToken.Should().NotBeNullOrWhiteSpace();
        tokenDto.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    // ── Refresh ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_WithValidToken_ReturnsNewTokens()
    {
        // First obtain tokens by logging in with a confirmed user
        var (user, password) = await _factory.SeedConfirmedUserAsync(
            username: $"refresh_{Guid.NewGuid():N}",
            email: $"refresh_{Guid.NewGuid():N}@example.com");

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginDto(user.UserName!, password));
        loginResponse.EnsureSuccessStatusCode();

        var tokens = await loginResponse.Content.ReadFromJsonAsync<TokenDto>(JsonOpts);
        tokens.Should().NotBeNull();

        // Now exchange the refresh token for a new pair
        var refreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh", tokens!.RefreshToken);

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var newTokens = await refreshResponse.Content.ReadFromJsonAsync<TokenDto>(JsonOpts);
        newTokens.Should().NotBeNull();
        newTokens!.AccessToken.Should().NotBeNullOrWhiteSpace();
        newTokens.RefreshToken.Should().NotBeNullOrWhiteSpace();
        // The rotated refresh token should be different from the original
        newTokens.RefreshToken.Should().NotBe(tokens.RefreshToken);
    }

    [Fact]
    public async Task Refresh_WithInvalidToken_Returns401()
    {
        var refreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh", "not-a-real-token");

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Logout ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_WithValidRefreshToken_Returns204()
    {
        var (user, password) = await _factory.SeedConfirmedUserAsync(
            username: $"logout_{Guid.NewGuid():N}",
            email: $"logout_{Guid.NewGuid():N}@example.com");

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginDto(user.UserName!, password));
        loginResponse.EnsureSuccessStatusCode();

        var tokens = await loginResponse.Content.ReadFromJsonAsync<TokenDto>(JsonOpts);

        var logoutResponse = await _client.PostAsJsonAsync("/api/auth/logout", tokens!.RefreshToken);

        logoutResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Logout_WithUnknownToken_Returns400()
    {
        var logoutResponse = await _client.PostAsJsonAsync("/api/auth/logout", "totally-unknown-token");

        logoutResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Refresh after logout ───────────────────────────────────────────────────

    [Fact]
    public async Task RefreshAfterLogout_WithRevokedToken_Returns401()
    {
        var (user, password) = await _factory.SeedConfirmedUserAsync(
            username: $"ral_{Guid.NewGuid():N}",
            email: $"ral_{Guid.NewGuid():N}@example.com");

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginDto(user.UserName!, password));
        var tokens = await loginResponse.Content.ReadFromJsonAsync<TokenDto>(JsonOpts);

        // Logout to revoke the token
        await _client.PostAsJsonAsync("/api/auth/logout", tokens!.RefreshToken);

        // Attempting to refresh with the now-revoked token must fail
        var refreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh", tokens.RefreshToken);

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
