using System.Net.Http.Json;

namespace WebClient.Services;

/// <summary>
/// Calls the backend /api/users/* endpoints.
/// Uses the "api" HttpClient which has the AuthHttpMessageHandler attached.
/// </summary>
public sealed class UserApiService : IUserApiService
{
    private readonly HttpClient _http;

    public UserApiService(IHttpClientFactory httpClientFactory)
    {
        _http = httpClientFactory.CreateClient("api");
    }

    public async Task<ApiResult<UserProfileDto>> GetProfileAsync()
    {
        try
        {
            var response = await _http.GetAsync("users/me");
            if (!response.IsSuccessStatusCode)
                return ApiResult<UserProfileDto>.Failure(await ReadErrorAsync(response));

            var profile = await response.Content.ReadFromJsonAsync<UserProfileDto>();
            return profile is null
                ? ApiResult<UserProfileDto>.Failure("Invalid response.")
                : ApiResult<UserProfileDto>.Success(profile);
        }
        catch (Exception ex)
        {
            return ApiResult<UserProfileDto>.Failure(ex.Message);
        }
    }

    public async Task<ApiResult<UserProfileDto>> UpdateProfileAsync(UpdateProfileRequest request)
    {
        try
        {
            var response = await _http.PutAsJsonAsync("users/me", request);
            if (!response.IsSuccessStatusCode)
                return ApiResult<UserProfileDto>.Failure(await ReadErrorAsync(response));

            var profile = await response.Content.ReadFromJsonAsync<UserProfileDto>();
            return profile is null
                ? ApiResult<UserProfileDto>.Failure("Invalid response.")
                : ApiResult<UserProfileDto>.Success(profile);
        }
        catch (Exception ex)
        {
            return ApiResult<UserProfileDto>.Failure(ex.Message);
        }
    }

    public async Task<ApiResult<string>> UploadProfileImageAsync(MultipartFormDataContent content)
    {
        try
        {
            var response = await _http.PutAsync("users/me/profile-image", content);
            if (!response.IsSuccessStatusCode)
                return ApiResult<string>.Failure(await ReadErrorAsync(response));

            var result = await response.Content.ReadFromJsonAsync<UrlResponse>();
            return result?.Url is not null
                ? ApiResult<string>.Success(result.Url)
                : ApiResult<string>.Failure("Invalid response.");
        }
        catch (Exception ex)
        {
            return ApiResult<string>.Failure(ex.Message);
        }
    }

    public async Task<ApiResult> DeleteProfileImageAsync()
    {
        try
        {
            var response = await _http.DeleteAsync("users/me/profile-image");
            return response.IsSuccessStatusCode
                ? ApiResult.Success()
                : ApiResult.Failure(await ReadErrorAsync(response));
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
            return body?.Error ?? body?.Title ?? response.ReasonPhrase ?? "An error occurred.";
        }
        catch
        {
            return response.ReasonPhrase ?? "An error occurred.";
        }
    }

    private record ErrorResponse(string? Error, string? Title);
    private record UrlResponse(string? Url);
}
