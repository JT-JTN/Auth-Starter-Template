using Application.DTOs.Auth;
using SharedKernel;

namespace Application.Services;

public interface IAuthService
{
    Task<Result> RegisterAsync(RegisterDto dto);
    Task<Result<TokenDto>> LoginAsync(LoginDto dto);
    Task<Result<TokenDto>> RefreshTokenAsync(string refreshToken);
    Task<Result> LogoutAsync(string refreshToken);
    Task<Result> ConfirmEmailAsync(string userId, string token);
    Task<Result> ChangePasswordAsync(string userId, ChangePasswordDto dto);
    Task<Result> ForgotPasswordAsync(string email);
    Task<Result> ResetPasswordAsync(ResetPasswordDto dto);
}
