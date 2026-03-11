using Application.DTOs.Auth;
using Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("register")]
    public async Task<IActionResult> RegisterAsync([FromBody] RegisterDto dto)
    {
        var result = await _authService.RegisterAsync(dto);
        if (result.IsFailure)
            return BadRequest(new { result.Error });

        return Ok(new { message = "Registration successful. Please check your email to confirm your account." });
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public async Task<IActionResult> LoginAsync([FromBody] LoginDto dto)
    {
        var result = await _authService.LoginAsync(dto);
        if (result.IsFailure)
            return Unauthorized(new { result.Error });

        return Ok(result.Value);
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshAsync([FromBody] string refreshToken)
    {
        var result = await _authService.RefreshTokenAsync(refreshToken);
        if (result.IsFailure)
            return Unauthorized(new { result.Error });

        return Ok(result.Value);
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> LogoutAsync([FromBody] string refreshToken)
    {
        var result = await _authService.LogoutAsync(refreshToken);
        if (result.IsFailure)
            return BadRequest(new { result.Error });

        return NoContent();
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpGet("confirm-email")]
    public async Task<IActionResult> ConfirmEmailAsync([FromQuery] string userId, [FromQuery] string token)
    {
        var result = await _authService.ConfirmEmailAsync(userId, token);
        if (result.IsFailure)
            return BadRequest(new { result.Error });

        return Ok(new { message = "Email confirmed successfully" });
    }

    [Authorize]
    [EnableRateLimiting("auth")]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePasswordAsync([FromBody] ChangePasswordDto dto)
    {
        var userId = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        var result = await _authService.ChangePasswordAsync(userId, dto);
        if (result.IsFailure)
            return BadRequest(new { result.Error });

        return NoContent();
    }

    [AllowAnonymous]
    [EnableRateLimiting("forgot-password")]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPasswordAsync([FromBody] ForgotPasswordDto dto)
    {
        await _authService.ForgotPasswordAsync(dto.Email);
        // Always 200 to prevent user enumeration
        return Ok(new { message = "If that email is registered you will receive a reset link" });
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPasswordAsync([FromBody] ResetPasswordDto dto)
    {
        var result = await _authService.ResetPasswordAsync(dto);
        if (result.IsFailure)
            return BadRequest(new { result.Error });

        return Ok(new { message = "Password has been reset successfully" });
    }
}
