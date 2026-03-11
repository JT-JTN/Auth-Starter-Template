namespace WebClient.Services;

public interface IPasskeyApiService
{
    // Login (anonymous)
    Task<ApiResult<string>> BeginLoginAsync(string? userName);
    Task<ApiResult<TokenDto>> CompleteLoginAsync(string credentialJson);

    // Registration (authenticated)
    Task<ApiResult<string>> BeginRegistrationAsync();
    Task<ApiResult> CompleteRegistrationAsync(string credentialJson);

    // Management (authenticated)
    Task<ApiResult<List<PasskeyInfoDto>>> GetPasskeysAsync();
    Task<ApiResult> RenamePasskeyAsync(string credentialId, string name);
    Task<ApiResult> RemovePasskeyAsync(string credentialId);
}
