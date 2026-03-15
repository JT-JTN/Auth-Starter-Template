using Application.DTOs.User;
using Application.Services;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Api.Controllers;

[ApiVersion("1.0")]
[Authorize]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[Produces("application/json")]
public class UsersController : ControllerBase
{
    private static readonly string[] AllowedImageTypes = ["image/jpeg", "image/png", "image/webp", "image/gif"];
    private const long MaxImageSizeBytes = 5 * 1024 * 1024; // 5 MB

    private readonly IUserProfileService _userProfileService;
    private readonly IAuthService _authService;

    public UsersController(IUserProfileService userProfileService, IAuthService authService)
    {
        _userProfileService = userProfileService;
        _authService = authService;
    }

    /// <summary>
    /// Returns the full profile of the currently authenticated user.
    /// </summary>
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("me")]
    public async Task<IActionResult> GetMeAsync()
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _userProfileService.GetProfileAsync(userId);
        if (result.IsFailure) return NotFound(new { result.Error });

        return Ok(result.Value);
    }

    /// <summary>
    /// Updates the profile of the currently authenticated user.
    /// </summary>
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPut("me")]
    public async Task<IActionResult> UpdateMeAsync([FromBody] UpdateUserProfileDto dto)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _userProfileService.UpdateProfileAsync(userId, dto);
        if (result.IsFailure) return BadRequest(new { result.Error });

        return Ok(result.Value);
    }

    /// <summary>
    /// Uploads a new profile image for the currently authenticated user.
    /// Accepted: JPEG, PNG, WebP, GIF — max 5 MB.
    /// </summary>
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPut("me/profile-image")]
    [RequestSizeLimit(MaxImageSizeBytes)]
    public async Task<IActionResult> UploadProfileImageAsync(IFormFile file)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        if (file is null || file.Length == 0)
            return BadRequest(new { Error = "No file provided." });

        if (file.Length > MaxImageSizeBytes)
            return BadRequest(new { Error = "Image exceeds the 5 MB size limit." });

        var contentType = file.ContentType.ToLowerInvariant();
        if (!AllowedImageTypes.Contains(contentType))
            return BadRequest(new { Error = "Unsupported image type. Allowed: JPEG, PNG, WebP, GIF." });

        await using var stream = file.OpenReadStream();
        var result = await _userProfileService.UpdateProfileImageAsync(userId, stream, contentType);
        if (result.IsFailure) return BadRequest(new { result.Error });

        return Ok(new { url = result.Value });
    }

    /// <summary>
    /// Deletes the profile image of the currently authenticated user.
    /// </summary>
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpDelete("me/profile-image")]
    public async Task<IActionResult> DeleteProfileImageAsync()
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _userProfileService.DeleteProfileImageAsync(userId);
        if (result.IsFailure) return BadRequest(new { result.Error });

        return NoContent();
    }

    /// <summary>
    /// Serves a user's profile image from database storage.
    /// Public endpoint — allows img tags to load images without auth headers.
    /// Only used when App:ProfileImageStorage = "Database".
    /// </summary>
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{userId}/profile-image")]
    public async Task<IActionResult> GetProfileImageAsync(string userId)
    {
        var result = await _userProfileService.GetProfileImageBytesAsync(userId);
        if (result is null) return NotFound();

        Response.Headers.CacheControl = "public, max-age=3600";
        return File(result.Value.Data, result.Value.ContentType);
    }

    /// <summary>
    /// Returns all active sessions for the current user.
    /// The current session is identified via the 'sid' claim in the JWT.
    /// </summary>
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("me/sessions")]
    public async Task<IActionResult> GetSessionsAsync()
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var currentSessionId = GetSessionId();
        var result = await _authService.GetSessionsAsync(userId, currentSessionId);
        if (result.IsFailure) return BadRequest(new { result.Error });

        return Ok(result.Value);
    }

    /// <summary>
    /// Revokes a single session by its ID.
    /// </summary>
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpDelete("me/sessions/{sessionId:guid}")]
    public async Task<IActionResult> RevokeSessionAsync(Guid sessionId)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _authService.RevokeSessionAsync(sessionId, userId);
        if (result.IsFailure) return BadRequest(new { result.Error });

        return NoContent();
    }

    /// <summary>
    /// Revokes all sessions except the current one.
    /// </summary>
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpDelete("me/sessions")]
    public async Task<IActionResult> RevokeAllOtherSessionsAsync()
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var currentSessionId = GetSessionId();
        var result = await _authService.RevokeAllOtherSessionsAsync(userId, currentSessionId);
        if (result.IsFailure) return BadRequest(new { result.Error });

        return NoContent();
    }

    /// <summary>
    /// Hard-deletes the current user's account and all associated personal data.
    /// </summary>
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpDelete("me")]
    public async Task<IActionResult> DeleteAccountAsync()
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _userProfileService.DeleteAccountAsync(userId);
        if (result.IsFailure) return BadRequest(new { result.Error });

        return NoContent();
    }

    /// <summary>
    /// Returns a full export of the current user's personal data as a JSON file.
    /// </summary>
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("me/export")]
    public async Task<IActionResult> ExportDataAsync()
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await _userProfileService.ExportDataAsync(userId);
        if (result.IsFailure) return BadRequest(new { result.Error });

        var json = System.Text.Json.JsonSerializer.Serialize(result.Value, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return File(bytes, "application/json", "account-export.json");
    }

    private string? GetUserId() =>
        User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

    private Guid? GetSessionId() =>
        Guid.TryParse(User.FindFirstValue("sid"), out var id) ? id : null;
}
