namespace WebClient.Services;

public interface IAuthApiService
{
    Task<ApiResult<TokenDto>> LoginAsync(LoginRequest request);
    Task<ApiResult> RegisterAsync(RegisterRequest request);
    Task<ApiResult> LogoutAsync(string refreshToken);
    Task<ApiResult> ForgotPasswordAsync(string email);
    Task<ApiResult> ResetPasswordAsync(ResetPasswordRequest request);
    Task<ApiResult> ChangePasswordAsync(ChangePasswordRequest request);
}
