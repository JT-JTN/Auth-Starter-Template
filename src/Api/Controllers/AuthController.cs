using Application.DTOs.Auth;
using Application.Services;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QRCoder;
using System.Security.Claims;

namespace Api.Controllers;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
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
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [HttpPost("login")]
    public async Task<IActionResult> LoginAsync([FromBody] LoginDto dto)
    {
        var result = await _authService.LoginAsync(dto);
        if (result.IsFailure)
            return Unauthorized(new { result.Error });

        var loginResult = result.Value!;

        if (loginResult.RequiresTwoFactor)
            return Ok(new { requiresTwoFactor = true, userId = loginResult.UserId });

        return Ok(new TokenDto(loginResult.AccessToken!, loginResult.RefreshToken!));
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(TokenDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshAsync([FromBody] string refreshToken)
    {
        var result = await _authService.RefreshTokenAsync(refreshToken);
        if (result.IsFailure)
            return Unauthorized(new { result.Error });

        return Ok(result.Value);
    }

    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
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
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
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
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
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
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPasswordAsync([FromBody] ForgotPasswordDto dto)
    {
        await _authService.ForgotPasswordAsync(dto.Email);
        // Always 200 to prevent user enumeration
        return Ok(new { message = "If that email is registered you will receive a reset link" });
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPasswordAsync([FromBody] ResetPasswordDto dto)
    {
        var result = await _authService.ResetPasswordAsync(dto);
        if (result.IsFailure)
            return BadRequest(new { result.Error });

        return Ok(new { message = "Password has been reset successfully" });
    }

    // ── Two-Factor Authentication ─────────────────────────────────────────────

    /// <summary>
    /// Returns the authenticator key and a base64 PNG QR code for the user to scan.
    /// Generates a new key if the user does not have one yet.
    /// </summary>
    [Authorize]
    [ProducesResponseType(typeof(TwoFactorSetupDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("2fa/setup")]
    public async Task<IActionResult> GetTwoFactorSetupAsync()
    {
        var userId = User.FindFirstValue("sub");
        if (userId is null) return Unauthorized();

        var result = await _authService.GetTwoFactorSetupAsync(userId);
        if (result.IsFailure)
            return BadRequest(new { result.Error });

        var setup = result.Value!;
        var qrCodeBase64 = GenerateQrCodeBase64(setup.AuthenticatorUri);

        return Ok(new TwoFactorSetupDto(setup.SharedKey, setup.AuthenticatorUri, qrCodeBase64));
    }

    /// <summary>
    /// Verifies the first TOTP code from the authenticator app and enables 2FA.
    /// </summary>
    [Authorize]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost("2fa/enable")]
    public async Task<IActionResult> EnableTwoFactorAsync([FromBody] TwoFactorCodeDto dto)
    {
        var userId = User.FindFirstValue("sub");
        if (userId is null) return Unauthorized();

        var result = await _authService.EnableTwoFactorAsync(userId, dto);
        if (result.IsFailure)
            return BadRequest(new { result.Error });

        return NoContent();
    }

    /// <summary>
    /// Verifies the current TOTP code, disables 2FA, and resets the authenticator key.
    /// </summary>
    [Authorize]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost("2fa/disable")]
    public async Task<IActionResult> DisableTwoFactorAsync([FromBody] TwoFactorCodeDto dto)
    {
        var userId = User.FindFirstValue("sub");
        if (userId is null) return Unauthorized();

        var result = await _authService.DisableTwoFactorAsync(userId, dto);
        if (result.IsFailure)
            return BadRequest(new { result.Error });

        return NoContent();
    }

    /// <summary>
    /// Completes a 2FA login. Called after POST /auth/login returns requiresTwoFactor=true.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(TokenDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost("2fa/verify")]
    public async Task<IActionResult> VerifyTwoFactorAsync([FromBody] TwoFactorVerifyDto dto)
    {
        var result = await _authService.VerifyTwoFactorAsync(dto);
        if (result.IsFailure)
            return Unauthorized(new { result.Error });

        return Ok(result.Value);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GenerateQrCodeBase64(string content)
    {
        using var generator = new QRCodeGenerator();
        var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(data);
        var bytes = qrCode.GetGraphic(5);
        return Convert.ToBase64String(bytes);
    }
}
