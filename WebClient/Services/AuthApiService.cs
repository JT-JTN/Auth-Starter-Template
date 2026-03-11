using System.Net.Http.Json;

namespace WebClient.Services;

/// <summary>
/// Calls the backend /api/auth/* endpoints.
/// Uses the "public" HttpClient (no auth handler — login/register don't need a token).
/// </summary>
public sealed class AuthApiService : IAuthApiService
{
    private readonly HttpClient _http;
    private readonly TokenStorageService _tokenStorage;
    private readonly JwtAuthenticationStateProvider _authStateProvider;

    public AuthApiService(
        IHttpClientFactory httpClientFactory,
        TokenStorageService tokenStorage,
        JwtAuthenticationStateProvider authStateProvider)
    {
        _http = httpClientFactory.CreateClient("public");
        _tokenStorage = tokenStorage;
        _authStateProvider = authStateProvider;
    }

    public async Task<ApiResult<TokenDto>> LoginAsync(LoginRequest request)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("auth/login", request);
            if (!response.IsSuccessStatusCode)
            {
                var err = await ReadErrorAsync(response);
                return ApiResult<TokenDto>.Failure(err);
            }

            var tokens = await response.Content.ReadFromJsonAsync<TokenDto>();
            if (tokens is null) return ApiResult<TokenDto>.Failure("Invalid response from server.");

            await _tokenStorage.SetTokensAsync(tokens.AccessToken, tokens.RefreshToken);
            _authStateProvider.NotifyStateChanged();
            return ApiResult<TokenDto>.Success(tokens);
        }
        catch (Exception ex)
        {
            return ApiResult<TokenDto>.Failure($"Login failed: {ex.Message}");
        }
    }

    public async Task<ApiResult> RegisterAsync(RegisterRequest request)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("auth/register", request);
            if (!response.IsSuccessStatusCode)
            {
                var err = await ReadErrorAsync(response);
                return ApiResult.Failure(err);
            }

            return ApiResult.Success();
        }
        catch (Exception ex)
        {
            return ApiResult.Failure($"Registration failed: {ex.Message}");
        }
    }

    public async Task<ApiResult> LogoutAsync(string refreshToken)
    {
        try
        {
            // Use authenticated client for logout (requires Bearer token)
            var response = await _http.PostAsJsonAsync("auth/logout", refreshToken);
            await _tokenStorage.ClearAsync();
            _authStateProvider.NotifyStateChanged();
            return ApiResult.Success();
        }
        catch
        {
            // Always clear locally even if the server call fails
            await _tokenStorage.ClearAsync();
            _authStateProvider.NotifyStateChanged();
            return ApiResult.Success();
        }
    }

    public async Task<ApiResult> ForgotPasswordAsync(string email)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("auth/forgot-password", new { Email = email });
            return ApiResult.Success(); // always 200 per backend spec
        }
        catch (Exception ex)
        {
            return ApiResult.Failure(ex.Message);
        }
    }

    public async Task<ApiResult> ResetPasswordAsync(ResetPasswordRequest request)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("auth/reset-password", request);
            if (!response.IsSuccessStatusCode)
                return ApiResult.Failure(await ReadErrorAsync(response));
            return ApiResult.Success();
        }
        catch (Exception ex)
        {
            return ApiResult.Failure(ex.Message);
        }
    }

    public async Task<ApiResult> ChangePasswordAsync(ChangePasswordRequest request)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("auth/change-password", request);
            if (!response.IsSuccessStatusCode)
                return ApiResult.Failure(await ReadErrorAsync(response));
            return ApiResult.Success();
        }
        catch (Exception ex)
        {
            return ApiResult.Failure(ex.Message);
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
            return body?.Error ?? response.ReasonPhrase ?? "An error occurred.";
        }
        catch
        {
            return response.ReasonPhrase ?? "An error occurred.";
        }
    }

    private record ErrorResponse(string? Error);
}
