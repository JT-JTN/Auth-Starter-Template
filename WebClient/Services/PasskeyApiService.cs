using System.Net.Http.Json;

namespace WebClient.Services;

public sealed class PasskeyApiService : IPasskeyApiService
{
    private readonly HttpClient _public;
    private readonly HttpClient _api;

    public PasskeyApiService(IHttpClientFactory factory)
    {
        _public = factory.CreateClient("public");
        _api    = factory.CreateClient("api");
    }

    public async Task<ApiResult<string>> BeginLoginAsync(string? userName)
    {
        try
        {
            var response = await _public.PostAsJsonAsync("auth/passkey/login/begin", new { userName });
            if (!response.IsSuccessStatusCode)
                return ApiResult<string>.Failure(await ReadErrorAsync(response));

            var json = await response.Content.ReadAsStringAsync();
            return ApiResult<string>.Success(json);
        }
        catch (Exception ex)
        {
            return ApiResult<string>.Failure(ex.Message);
        }
    }

    public async Task<ApiResult<TokenDto>> CompleteLoginAsync(string credentialJson)
    {
        try
        {
            var response = await _public.PostAsJsonAsync("auth/passkey/login/complete",
                new { credentialJson });

            if (!response.IsSuccessStatusCode)
                return ApiResult<TokenDto>.Failure(await ReadErrorAsync(response));

            var token = await response.Content.ReadFromJsonAsync<TokenDto>();
            return token is null
                ? ApiResult<TokenDto>.Failure("Invalid response.")
                : ApiResult<TokenDto>.Success(token);
        }
        catch (Exception ex)
        {
            return ApiResult<TokenDto>.Failure(ex.Message);
        }
    }

    public async Task<ApiResult<string>> BeginRegistrationAsync()
    {
        try
        {
            var response = await _api.PostAsync("auth/passkey/register/begin", null);
            if (!response.IsSuccessStatusCode)
                return ApiResult<string>.Failure(await ReadErrorAsync(response));

            var json = await response.Content.ReadAsStringAsync();
            return ApiResult<string>.Success(json);
        }
        catch (Exception ex)
        {
            return ApiResult<string>.Failure(ex.Message);
        }
    }

    public async Task<ApiResult> CompleteRegistrationAsync(string credentialJson)
    {
        try
        {
            var response = await _api.PostAsJsonAsync("auth/passkey/register/complete",
                new { credentialJson });

            return response.IsSuccessStatusCode
                ? ApiResult.Success()
                : ApiResult.Failure(await ReadErrorAsync(response));
        }
        catch (Exception ex)
        {
            return ApiResult.Failure(ex.Message);
        }
    }

    public async Task<ApiResult<List<PasskeyInfoDto>>> GetPasskeysAsync()
    {
        try
        {
            var response = await _api.GetAsync("auth/passkey");
            if (!response.IsSuccessStatusCode)
                return ApiResult<List<PasskeyInfoDto>>.Failure(await ReadErrorAsync(response));

            var list = await response.Content.ReadFromJsonAsync<List<PasskeyInfoDto>>();
            return ApiResult<List<PasskeyInfoDto>>.Success(list ?? []);
        }
        catch (Exception ex)
        {
            return ApiResult<List<PasskeyInfoDto>>.Failure(ex.Message);
        }
    }

    public async Task<ApiResult> RenamePasskeyAsync(string credentialId, string name)
    {
        try
        {
            var response = await _api.PutAsJsonAsync("auth/passkey/rename",
                new { credentialId, name });
            return response.IsSuccessStatusCode
                ? ApiResult.Success()
                : ApiResult.Failure(await ReadErrorAsync(response));
        }
        catch (Exception ex)
        {
            return ApiResult.Failure(ex.Message);
        }
    }

    public async Task<ApiResult> RemovePasskeyAsync(string credentialId)
    {
        try
        {
            var response = await _api.DeleteAsync($"auth/passkey?credentialId={Uri.EscapeDataString(credentialId)}");
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
}
