using Application.DTOs.Auth;
using Application.Services;
using Domain.Users;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Infrastructure.Persistance;
using Infrastructure.Persistance.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SharedKernel;
using System.Text;
using System.Text.Json;

namespace Infrastructure.Services;

public sealed class PasskeyService : IPasskeyService
{
    private readonly IFido2 _fido2;
    private readonly IMemoryCache _cache;
    private readonly UserManager<User> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly ITokenRepository _tokenRepository;
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _dateTime;
    private readonly IHttpContextAccessor _httpContextAccessor;

    private const string RegPrefix = "passkey:reg:";
    private const string LoginPrefix = "passkey:login:";
    private static readonly TimeSpan ChallengeExpiry = TimeSpan.FromMinutes(5);

    public PasskeyService(
        IFido2 fido2,
        IMemoryCache cache,
        UserManager<User> userManager,
        ApplicationDbContext db,
        ITokenService tokenService,
        ITokenRepository tokenRepository,
        IUnitOfWork uow,
        IDateTimeProvider dateTime,
        IHttpContextAccessor httpContextAccessor)
    {
        _fido2 = fido2;
        _cache = cache;
        _userManager = userManager;
        _db = db;
        _tokenService = tokenService;
        _tokenRepository = tokenRepository;
        _uow = uow;
        _dateTime = dateTime;
        _httpContextAccessor = httpContextAccessor;
    }

    private string? GetClientIp() =>
        _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    private string? GetUserAgent() =>
        _httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString();

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>
    /// Decodes clientDataJSON and extracts the challenge field (base64url string).
    /// Used as the server-side cache key to link begin→complete without any cookies.
    /// </summary>
    private static string ExtractChallenge(byte[] clientDataJsonBytes)
    {
        var json = Encoding.UTF8.GetString(clientDataJsonBytes);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("challenge").GetString()
            ?? throw new InvalidOperationException("No challenge field in clientDataJSON");
    }

    // ── Registration ──────────────────────────────────────────────────────────

    public async Task<Result<string>> BeginRegistrationAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return Result.Failure<string>("User not found");

        var fido2User = new Fido2User
        {
            Id = Encoding.UTF8.GetBytes(user.Id),
            Name = user.UserName!,
            DisplayName = user.Email ?? user.UserName!
        };

        // Exclude credentials the user already has (prevents duplicates on same authenticator)
        var existingKeys = await _db.PasskeyCredentials
            .Where(p => p.UserId == user.Id)
            .Select(p => new PublicKeyCredentialDescriptor(p.CredentialId))
            .ToListAsync();

        var authSelection = new AuthenticatorSelection
        {
            UserVerification = UserVerificationRequirement.Preferred,
            ResidentKey = ResidentKeyRequirement.Preferred
        };

        var options = _fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = fido2User,
            ExcludeCredentials = existingKeys,
            AuthenticatorSelection = authSelection,
            AttestationPreference = AttestationConveyancePreference.None
        });

        // Store by challenge so we can look it up in CompleteRegistrationAsync
        var challengeKey = ToBase64Url(options.Challenge);
        _cache.Set(RegPrefix + challengeKey, options.ToJson(), ChallengeExpiry);

        return Result.Success(options.ToJson());
    }

    public async Task<Result> CompleteRegistrationAsync(string userId, string credentialJson)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return Result.Failure("User not found");

        AuthenticatorAttestationRawResponse attestationResponse;
        try
        {
            attestationResponse = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(credentialJson)
                ?? throw new JsonException("Null result");
        }
        catch (Exception ex)
        {
            return Result.Failure($"Invalid credential format: {ex.Message}");
        }

        // Extract challenge from clientDataJSON to retrieve stored options
        string challengeKey;
        try { challengeKey = ExtractChallenge(attestationResponse.Response.ClientDataJson); }
        catch (Exception ex) { return Result.Failure($"Could not read challenge: {ex.Message}"); }

        var optionsJson = _cache.Get<string>(RegPrefix + challengeKey);
        if (optionsJson is null)
            return Result.Failure("Registration session expired or not found. Please try again.");

        _cache.Remove(RegPrefix + challengeKey);

        var options = CredentialCreateOptions.FromJson(optionsJson);

        RegisteredPublicKeyCredential credential;
        try
        {
            credential = await _fido2.MakeNewCredentialAsync(
                new MakeNewCredentialParams
                {
                    AttestationResponse = attestationResponse,
                    OriginalOptions = options,
                    IsCredentialIdUniqueToUserCallback = async (args, ct) =>
                        !await _db.PasskeyCredentials
                            .AnyAsync(p => p.CredentialId == args.CredentialId, ct)
                });
        }
        catch (Exception ex)
        {
            return Result.Failure(ex.Message);
        }

        var transportsJson = credential.Transports is { Length: > 0 }
            ? JsonSerializer.Serialize(credential.Transports.Select(t => t.ToEnumMemberValue()))
            : null;

        _db.PasskeyCredentials.Add(new PasskeyCredential
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CredentialId = credential.Id,
            PublicKey = credential.PublicKey,
            SignCount = credential.SignCount,
            AaGuid = credential.AaGuid,
            Transports = transportsJson,
            Name = $"Passkey {DateTimeOffset.UtcNow:yyyy-MM-dd}",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await _uow.SaveChangesAsync();
        return Result.Success();
    }

    // ── Login ─────────────────────────────────────────────────────────────────

    public async Task<Result<string>> BeginLoginAsync(string? userName)
    {
        List<PublicKeyCredentialDescriptor> allowedCredentials = [];

        if (!string.IsNullOrWhiteSpace(userName))
        {
            var user = await _userManager.FindByNameAsync(userName)
                       ?? await _userManager.FindByEmailAsync(userName);

            if (user is not null)
            {
                allowedCredentials = await _db.PasskeyCredentials
                    .Where(p => p.UserId == user.Id)
                    .Select(p => new PublicKeyCredentialDescriptor(p.CredentialId))
                    .ToListAsync();
            }
        }

        // Empty list → discoverable credential (usernameless) flow
        var options = _fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = allowedCredentials,
            UserVerification = UserVerificationRequirement.Preferred
        });

        var challengeKey = ToBase64Url(options.Challenge);
        _cache.Set(LoginPrefix + challengeKey, options.ToJson(), ChallengeExpiry);

        return Result.Success(options.ToJson());
    }

    public async Task<Result<TokenDto>> CompleteLoginAsync(string credentialJson)
    {
        AuthenticatorAssertionRawResponse assertionResponse;
        try
        {
            assertionResponse = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(credentialJson)
                ?? throw new JsonException("Null result");
        }
        catch (Exception ex)
        {
            return Result.Failure<TokenDto>($"Invalid credential format: {ex.Message}");
        }

        string challengeKey;
        try { challengeKey = ExtractChallenge(assertionResponse.Response.ClientDataJson); }
        catch (Exception ex) { return Result.Failure<TokenDto>($"Could not read challenge: {ex.Message}"); }

        var optionsJson = _cache.Get<string>(LoginPrefix + challengeKey);
        if (optionsJson is null)
            return Result.Failure<TokenDto>("Login session expired. Please try again.");

        _cache.Remove(LoginPrefix + challengeKey);

        var options = AssertionOptions.FromJson(optionsJson);

        // Find the stored credential — RawId is the byte[] version of the base64url Id
        var credentialIdBytes = assertionResponse.RawId;
        var storedCred = await _db.PasskeyCredentials
            .FirstOrDefaultAsync(p => p.CredentialId == credentialIdBytes);

        if (storedCred is null)
            return Result.Failure<TokenDto>("Passkey not found. It may have been removed.");

        VerifyAssertionResult verifyResult;
        try
        {
            verifyResult = await _fido2.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = assertionResponse,
                OriginalOptions = options,
                StoredPublicKey = storedCred.PublicKey,
                StoredSignatureCounter = (uint)storedCred.SignCount,
                IsUserHandleOwnerOfCredentialIdCallback = async (args, ct) =>
                {
                    if (args.UserHandle is null) return true;
                    var claimedUserId = Encoding.UTF8.GetString(args.UserHandle);
                    return await _db.PasskeyCredentials
                        .AnyAsync(p => p.CredentialId == args.CredentialId && p.UserId == claimedUserId, ct);
                }
            });
        }
        catch (Exception ex)
        {
            return Result.Failure<TokenDto>(ex.Message);
        }

        // Update signature counter (replay-attack protection)
        storedCred.SignCount = verifyResult.SignCount;

        var user = await _userManager.FindByIdAsync(storedCred.UserId);
        if (user is null)
            return Result.Failure<TokenDto>("User not found");

        if (!user.EmailConfirmed)
            return Result.Failure<TokenDto>("Email address has not been confirmed");

        user.LastLoginUtc = _dateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        var refreshToken = _tokenService.GenerateRefreshToken(user.Id, GetClientIp(), GetUserAgent());
        var roles = await _userManager.GetRolesAsync(user);
        var accessToken = await _tokenService.GenerateAccessToken(user, roles, refreshToken.Id);
        await _tokenRepository.SaveRefreshTokenAsync(refreshToken);
        await _uow.SaveChangesAsync();

        return Result.Success(new TokenDto(accessToken, refreshToken.Token));
    }

    // ── Management ────────────────────────────────────────────────────────────

    public async Task<Result<List<PasskeyInfoDto>>> GetPasskeysAsync(string userId)
    {
        var creds = await _db.PasskeyCredentials
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync();

        var dtos = creds
            .Select(p => new PasskeyInfoDto(
                Convert.ToBase64String(p.CredentialId),
                p.Name ?? "Passkey",
                p.CreatedAt.UtcDateTime))
            .ToList();

        return Result.Success(dtos);
    }

    public async Task<Result> RemovePasskeyAsync(string userId, string credentialIdBase64)
    {
        byte[] credentialId;
        try { credentialId = Convert.FromBase64String(credentialIdBase64); }
        catch { return Result.Failure("Invalid credential ID format"); }

        var cred = await _db.PasskeyCredentials
            .FirstOrDefaultAsync(p => p.UserId == userId && p.CredentialId == credentialId);

        if (cred is null)
            return Result.Failure("Passkey not found");

        _db.PasskeyCredentials.Remove(cred);
        await _uow.SaveChangesAsync();

        return Result.Success();
    }
}
