using Application.DTOs.Auth;
using Application.Services;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Api.Controllers;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth/passkey")]
[ApiController]
[Produces("application/json")]
public class PasskeyController : ControllerBase
{
    private readonly IPasskeyService _passkeyService;

    public PasskeyController(IPasskeyService passkeyService)
    {
        _passkeyService = passkeyService;
    }

    // -------------------------------------------------------------------------
    // Authentication (anonymous — this is how users log in with a passkey)
    // -------------------------------------------------------------------------

    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost("login/begin")]
    public async Task<IActionResult> BeginLoginAsync([FromBody] PasskeyBeginLoginDto dto)
    {
        var result = await _passkeyService.BeginLoginAsync(dto.UserName);
        if (result.IsFailure) return BadRequest(new { result.Error });

        // Return the raw JSON options exactly as the WebAuthn API expects them
        return Content(result.Value, "application/json");
    }

    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost("login/complete")]
    public async Task<IActionResult> CompleteLoginAsync([FromBody] PasskeyCompleteLoginDto dto)
    {
        var result = await _passkeyService.CompleteLoginAsync(dto.CredentialJson);
        if (result.IsFailure) return Unauthorized(new { result.Error });

        return Ok(result.Value);
    }

    // -------------------------------------------------------------------------
    // Registration (requires an authenticated user — adding a passkey to an account)
    // -------------------------------------------------------------------------

    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost("register/begin")]
    public async Task<IActionResult> BeginRegistrationAsync()
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _passkeyService.BeginRegistrationAsync(userId);
        if (result.IsFailure) return BadRequest(new { result.Error });

        return Content(result.Value, "application/json");
    }

    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost("register/complete")]
    public async Task<IActionResult> CompleteRegistrationAsync([FromBody] PasskeyCompleteRegistrationDto dto)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _passkeyService.CompleteRegistrationAsync(userId, dto.CredentialJson);
        if (result.IsFailure) return BadRequest(new { result.Error });

        return Ok(new { message = "Passkey registered successfully" });
    }

    // -------------------------------------------------------------------------
    // Management (requires authenticated user)
    // -------------------------------------------------------------------------

    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet]
    public async Task<IActionResult> GetPasskeysAsync()
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _passkeyService.GetPasskeysAsync(userId);
        if (result.IsFailure) return BadRequest(new { result.Error });

        return Ok(result.Value);
    }

    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPut("rename")]
    public async Task<IActionResult> RenamePasskeyAsync([FromBody] PasskeyRenameDto dto)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _passkeyService.RenamePasskeyAsync(userId, dto.CredentialId, dto.Name);
        if (result.IsFailure) return BadRequest(new { result.Error });

        return NoContent();
    }

    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpDelete]
    public async Task<IActionResult> RemovePasskeyAsync([FromQuery] string credentialId)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _passkeyService.RemovePasskeyAsync(userId, credentialId);
        if (result.IsFailure) return BadRequest(new { result.Error });

        return NoContent();
    }

    private string? GetUserId() =>
        User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
}
