namespace Domain.Users;

public sealed class PasskeyCredential
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = default!;
    public User User { get; set; } = default!;

    /// <summary>Raw credential ID bytes from the authenticator.</summary>
    public byte[] CredentialId { get; set; } = [];

    /// <summary>COSE-encoded public key bytes.</summary>
    public byte[] PublicKey { get; set; } = [];

    /// <summary>Signature counter for replay-attack prevention.</summary>
    public long SignCount { get; set; }

    /// <summary>Authenticator Attestation GUID identifying the device model.</summary>
    public Guid AaGuid { get; set; }

    /// <summary>JSON-serialized transport hints (e.g. ["internal","hybrid"]).</summary>
    public string? Transports { get; set; }

    /// <summary>User-visible label (e.g. "Passkey 2026-03-11").</summary>
    public string? Name { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
