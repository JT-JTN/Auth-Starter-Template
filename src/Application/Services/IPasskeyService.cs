using Application.DTOs.Auth;
using SharedKernel;

namespace Application.Services;

public interface IPasskeyService
{
    // Registration (requires authenticated user)
    Task<Result<string>> BeginRegistrationAsync(string userId);
    Task<Result> CompleteRegistrationAsync(string userId, string credentialJson);

    // Authentication (anonymous)
    Task<Result<string>> BeginLoginAsync(string? userName);
    Task<Result<TokenDto>> CompleteLoginAsync(string credentialJson);

    // Management (requires authenticated user)
    Task<Result<List<PasskeyInfoDto>>> GetPasskeysAsync(string userId);
    Task<Result> RenamePasskeyAsync(string userId, string credentialIdBase64, string name);
    Task<Result> RemovePasskeyAsync(string userId, string credentialIdBase64);
}
