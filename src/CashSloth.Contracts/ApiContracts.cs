using System.Text.Json.Serialization;

namespace CashSloth.Contracts;

public enum CashSlothRole
{
    User = 0,
    Creator = 1,
    Admin = 2
}

public static class CashSlothRoles
{
    public const string User = nameof(CashSlothRole.User);
    public const string Creator = nameof(CashSlothRole.Creator);
    public const string Admin = nameof(CashSlothRole.Admin);

    public static readonly string[] All = [User, Creator, Admin];
}

public sealed record ApiError(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string[]>? FieldErrors,
    string TraceId);

public sealed record ServerInfoResponse(
    string ServerId,
    string Version,
    string PublicUrl,
    string KeyId,
    DateTimeOffset UtcNow);

public sealed record ServerTrustDocument(
    int Version,
    string ServerId,
    string HttpsUrl,
    string PublicKey,
    string KeyId,
    string Fingerprint);

public sealed record DevicePairRequest(
    string PairingCode,
    string DeviceName,
    string PublicKey,
    string Signature);

public sealed record DevicePairResponse(Guid DeviceId, string DeviceName, DateTimeOffset PairedAtUtc);

public sealed record DeviceChallengeRequest(Guid DeviceId, string Purpose);

public sealed record DeviceChallengeResponse(
    Guid ChallengeId,
    string Nonce,
    string Purpose,
    DateTimeOffset ExpiresAtUtc);

public sealed record DeviceProof(Guid DeviceId, Guid ChallengeId, string Signature);

public sealed record RegisterRequest(string Username, string Password, DeviceProof Proof);

public sealed record LoginRequest(string Username, string Password, DeviceProof Proof);

public sealed record RefreshRequest(string RefreshToken, DeviceProof Proof);

public sealed record LogoutRequest(string? RefreshToken);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record AuthTokenResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    UserProfileResponse User);

public sealed record UserProfileResponse(
    string Id,
    string Username,
    string Role,
    bool IsApproved,
    bool IsActive,
    bool MustChangePassword,
    Guid DeviceId,
    Guid SessionId);

public sealed record RegistrationResponse(string UserId, string Username, bool IsApproved);

public sealed record PresetItemDocument(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("unit_cents")] long UnitCents,
    [property: JsonPropertyName("category")] string Category);

public sealed record PresetDocument(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("categories")] string[] Categories,
    [property: JsonPropertyName("items")] PresetItemDocument[] Items,
    [property: JsonPropertyName("version")] long Version = 0,
    [property: JsonPropertyName("is_active")] bool IsActive = false,
    [property: JsonPropertyName("updated_at_utc")] DateTimeOffset? UpdatedAtUtc = null);

public sealed record PresetSummaryResponse(
    string Id,
    string Name,
    long Version,
    bool IsActive,
    int ItemCount,
    DateTimeOffset UpdatedAtUtc);

public sealed record PresetWriteResponse(string Id, long Version, bool IsActive);

public sealed record ExchangeRateResponse(
    string BaseCurrency,
    DateOnly RateDate,
    DateTimeOffset FetchedAtUtc,
    bool IsStale,
    IReadOnlyDictionary<string, decimal> Rates);

public sealed record TranslationResolveRequest(string SourceLanguage, string TargetLanguage, string[] Texts);

public sealed record TranslationResolution(string SourceText, string? TranslatedText, bool Found);

public sealed record TranslationResolveResponse(
    string SourceLanguage,
    string TargetLanguage,
    TranslationResolution[] Results);

public sealed record TranslationUpsertRequest(
    string SourceLanguage,
    string TargetLanguage,
    string SourceText,
    string TranslatedText);

public sealed record AdminAccountResponse(
    string Id,
    string Username,
    string Role,
    bool IsApproved,
    bool IsActive,
    bool MustChangePassword,
    int AccessFailedCount,
    DateTimeOffset? LockoutEndUtc,
    DateTimeOffset CreatedAtUtc);

public sealed record AdminAccountApprovalRequest(bool IsApproved);

public sealed record AdminAccountRoleRequest(string Role);

public sealed record AdminAccountStatusRequest(bool IsActive);

public sealed record AdminPasswordResetResponse(string TemporaryPassword);

public sealed record AdminDeviceResponse(
    Guid Id,
    string Name,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastSeenAtUtc,
    string PublicKeyFingerprint);

public sealed record AdminDeviceRenameRequest(string Name);

public sealed record AdminDeviceStatusRequest(bool IsActive);

public sealed record AuditEventResponse(
    long Id,
    DateTimeOffset CreatedAtUtc,
    string Actor,
    string Action,
    string TargetType,
    string? TargetId,
    string? Detail,
    string? TraceId);
