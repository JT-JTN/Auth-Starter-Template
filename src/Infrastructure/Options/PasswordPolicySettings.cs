namespace Infrastructure.Options;

public sealed class PasswordPolicySettings
{
    public const string SectionName = "PasswordPolicy";

    public int RequiredLength { get; init; } = 8;
    public bool RequireDigit { get; init; } = true;
    public bool RequireUppercase { get; init; } = true;
    public bool RequireLowercase { get; init; } = true;
    public bool RequireNonAlphanumeric { get; init; } = true;
    public int RequiredUniqueChars { get; init; } = 1;
}
