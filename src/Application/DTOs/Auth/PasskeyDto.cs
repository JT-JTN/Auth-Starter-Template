namespace Application.DTOs.Auth;

public record PasskeyBeginLoginDto(string? UserName);
public record PasskeyCompleteLoginDto(string CredentialJson);
public record PasskeyCompleteRegistrationDto(string CredentialJson);
public record PasskeyInfoDto(string CredentialId, string? Name, DateTimeOffset CreatedAt);
public record PasskeyRenameDto(string CredentialId, string Name);
