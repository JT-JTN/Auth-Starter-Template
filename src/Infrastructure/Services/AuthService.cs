using Application.DTOs.Auth;
using Application.Services;
using Domain.Users;
using Infrastructure.Persistance.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using SharedKernel;
using System.Text;

namespace Infrastructure.Services;

public sealed class AuthService : IAuthService
{
    private readonly UserManager<User> _userManager;
    private readonly ITokenService _tokenService;
    private readonly ITokenRepository _tokenRepository;
    private readonly IAppEmailSender _emailSender;
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _dateTime;

    public AuthService(
        UserManager<User> userManager,
        ITokenService tokenService,
        ITokenRepository tokenRepository,
        IAppEmailSender emailSender,
        IUnitOfWork uow,
        IDateTimeProvider dateTime)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _tokenRepository = tokenRepository;
        _emailSender = emailSender;
        _uow = uow;
        _dateTime = dateTime;
    }

    public async Task<Result> RegisterAsync(RegisterDto dto)
    {
        if (dto.Password != dto.ConfirmPassword)
            return Result.Failure("Passwords do not match");

        var user = new User
        {
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            UserName = dto.UserName,
            Email = dto.Email,
            CreatedAtUtc = _dateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            return Result.Failure(errors);
        }

        // Send confirmation email. Email is best-effort — failure does not fail registration.
        var confirmToken = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(confirmToken));
        await _emailSender.SendEmailConfirmationAsync(user.Email!, user.UserName!, user.Id, encodedToken);

        return Result.Success();
    }

    public async Task<Result<TokenDto>> LoginAsync(LoginDto dto)
    {
        var user = await _userManager.FindByNameAsync(dto.UserName)
                   ?? await _userManager.FindByEmailAsync(dto.UserName);

        if (user is null)
            return Result.Failure<TokenDto>("Invalid credentials");

        if (await _userManager.IsLockedOutAsync(user))
            return Result.Failure<TokenDto>("Account is locked. Try again later.");

        if (!await _userManager.CheckPasswordAsync(user, dto.Password))
        {
            await _userManager.AccessFailedAsync(user);
            return Result.Failure<TokenDto>("Invalid credentials");
        }

        if (!user.EmailConfirmed)
            return Result.Failure<TokenDto>("Email address has not been confirmed");

        await _userManager.ResetAccessFailedCountAsync(user);

        var roles = await _userManager.GetRolesAsync(user);
        var accessToken = await _tokenService.GenerateAccessToken(user, roles);
        var newRefreshToken = _tokenService.GenerateRefreshToken(user.Id);

        // Atomic: update LastLoginUtc and persist the new refresh token together.
        await _uow.BeginTransactionAsync();
        user.LastLoginUtc = _dateTime.UtcNow;
        await _userManager.UpdateAsync(user);
        await _tokenRepository.SaveRefreshTokenAsync(newRefreshToken);
        await _uow.CommitTransactionAsync();

        return Result.Success(new TokenDto(accessToken, newRefreshToken.Token));
    }

    public async Task<Result<TokenDto>> RefreshTokenAsync(string refreshToken)
    {
        var storedToken = await _tokenRepository.GetRefreshTokenAsync(refreshToken);

        if (storedToken is null)
            return Result.Failure<TokenDto>("Invalid refresh token");

        // A revoked token being reused is a strong signal of theft.
        // Revoke all sessions for this user as a security response.
        if (storedToken.RevokedAtUtc.HasValue)
        {
            await _tokenRepository.DeleteAllRefreshTokensForUserAsync(storedToken.UserId);
            await _uow.SaveChangesAsync();
            return Result.Failure<TokenDto>("Security violation detected. All sessions have been revoked. Please log in again.");
        }

        if (storedToken.IsExpired)
            return Result.Failure<TokenDto>("Refresh token has expired. Please log in again.");

        var user = storedToken.User;
        if (user is null)
            return Result.Failure<TokenDto>("User account no longer exists");

        var roles = await _userManager.GetRolesAsync(user);
        var newAccessToken = await _tokenService.GenerateAccessToken(user, roles);
        var newRefreshToken = _tokenService.GenerateRefreshToken(user.Id);

        // Atomic: revoke old token and issue new one in a single transaction.
        await _uow.BeginTransactionAsync();
        storedToken.RevokedAtUtc = _dateTime.UtcNow;
        await _tokenRepository.UpdateRefreshTokenAsync(storedToken);
        await _tokenRepository.SaveRefreshTokenAsync(newRefreshToken);
        await _uow.CommitTransactionAsync();

        return Result.Success(new TokenDto(newAccessToken, newRefreshToken.Token));
    }

    public async Task<Result> LogoutAsync(string refreshToken)
    {
        var storedToken = await _tokenRepository.GetRefreshTokenAsync(refreshToken);

        if (storedToken is null)
            return Result.Failure("Token not found");

        if (!storedToken.IsActive)
            return Result.Success();

        storedToken.RevokedAtUtc = _dateTime.UtcNow;
        await _tokenRepository.UpdateRefreshTokenAsync(storedToken);
        await _uow.SaveChangesAsync();

        return Result.Success();
    }

    public async Task<Result> ConfirmEmailAsync(string userId, string token)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return Result.Failure("User not found");

        string decodedToken;
        try { decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token)); }
        catch { return Result.Failure("Invalid confirmation token"); }

        var result = await _userManager.ConfirmEmailAsync(user, decodedToken);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            return Result.Failure(errors);
        }

        return Result.Success();
    }

    public async Task<Result> ChangePasswordAsync(string userId, ChangePasswordDto dto)
    {
        if (dto.NewPassword != dto.ConfirmNewPassword)
            return Result.Failure("New passwords do not match");

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return Result.Failure("User not found");

        var result = await _userManager.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            return Result.Failure(errors);
        }

        return Result.Success();
    }

    public async Task<Result> ForgotPasswordAsync(string email)
    {
        var user = await _userManager.FindByEmailAsync(email);

        // Always return success to prevent user enumeration
        if (user is null || !user.EmailConfirmed)
            return Result.Success();

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        await _emailSender.SendPasswordResetAsync(user.Email!, user.UserName!, encodedToken);

        return Result.Success();
    }

    public async Task<Result> ResetPasswordAsync(ResetPasswordDto dto)
    {
        if (dto.NewPassword != dto.ConfirmNewPassword)
            return Result.Failure("Passwords do not match");

        var user = await _userManager.FindByEmailAsync(dto.Email);
        if (user is null)
            return Result.Failure("Invalid request");

        string decodedToken;
        try { decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(dto.Token)); }
        catch { return Result.Failure("Invalid reset token"); }

        var result = await _userManager.ResetPasswordAsync(user, decodedToken, dto.NewPassword);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            return Result.Failure(errors);
        }

        return Result.Success();
    }
}
