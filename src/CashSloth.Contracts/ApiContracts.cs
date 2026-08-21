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

public enum CashSlothEventState
{
    Draft = 0,
    Active = 1,
    Closing = 2,
    Ended = 3,
    Cancelled = 4
}

public enum CashSlothEventRole
{
    Participant = 0,
    Host = 1
}

public enum CashSlothEventJoinMode
{
    Open = 0,
    Code = 1
}

public enum CashSlothEventMemberStatus
{
    Active = 0,
    Left = 1,
    Kicked = 2
}

public enum EventSaleSyncDisposition
{
    Accepted = 0,
    Duplicate = 1,
    Rejected = 2
}

public sealed record EventRulesDocument(
    string[] AllowedPaymentMethods,
    bool AllowTips,
    bool AllowShowcase);

public sealed record EventCreateRequest(
    string Name,
    string HostNickname,
    string PresetId,
    long PresetVersion,
    CashSlothEventJoinMode JoinMode,
    EventRulesDocument Rules);

public sealed record EventUpdateDraftRequest(
    string Name,
    string HostNickname,
    string PresetId,
    long PresetVersion,
    CashSlothEventJoinMode JoinMode,
    EventRulesDocument Rules,
    long Version);

public sealed record EventMemberResponse(
    Guid Id,
    string UserId,
    Guid DeviceId,
    CashSlothEventRole Role,
    CashSlothEventMemberStatus Status,
    string Nickname,
    bool IsOnline,
    DateTimeOffset JoinedAtUtc,
    DateTimeOffset? LastSeenAtUtc,
    DateTimeOffset? LeftAtUtc,
    DateTimeOffset? KickedAtUtc,
    int PendingSaleCount);

public sealed record EventSummaryResponse(
    Guid Id,
    string Name,
    CashSlothEventState State,
    string HostUsername,
    CashSlothEventJoinMode JoinMode,
    int ActiveMemberCount,
    DateTimeOffset? StartedAtUtc);

public sealed record EventDetailResponse(
    Guid Id,
    string Name,
    CashSlothEventState State,
    string HostUserId,
    string HostUsername,
    string HostNickname,
    string PresetId,
    long PresetVersion,
    string PresetHash,
    PresetDocument? Preset,
    CashSlothEventJoinMode JoinMode,
    EventRulesDocument Rules,
    long Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? SalesCutoffUtc,
    DateTimeOffset? EndedAtUtc,
    EventMemberResponse[] Members);

public sealed record EventPublishResponse(
    EventDetailResponse Event,
    EventMemberResponse Membership,
    string OfflineLease,
    DateTimeOffset OfflineUntilUtc,
    string? JoinCode);

public sealed record EventJoinRequest(string Nickname, string? JoinCode);

public sealed record EventMembershipResponse(
    EventDetailResponse Event,
    EventMemberResponse Membership,
    string OfflineLease,
    DateTimeOffset OfflineUntilUtc);

public sealed record EventMemberRenameRequest(string Nickname);

public sealed record EventHeartbeatRequest(int PendingSaleCount);

public sealed record EventHeartbeatResponse(
    CashSlothEventState State,
    DateTimeOffset? SalesCutoffUtc,
    string Nickname,
    DateTimeOffset ServerUtcNow,
    string OfflineLease,
    DateTimeOffset OfflineUntilUtc);

public sealed record EventSaleLineUpload(
    string ItemId,
    string Name,
    long UnitCents,
    int Quantity,
    long LineTotalCents);

public sealed record EventSaleUpload(
    string ClientSaleId,
    DateTimeOffset CompletedAtUtc,
    string PaymentMethod,
    bool IsShowcase,
    long SubtotalCents,
    long TipCents,
    long TotalCents,
    long GivenCents,
    long ChangeCents,
    EventSaleLineUpload[] Lines);

public sealed record EventSaleBatchRequest(Guid MemberId, EventSaleUpload[] Sales);

public sealed record EventSaleUploadResult(
    string ClientSaleId,
    EventSaleSyncDisposition Disposition,
    string? ErrorCode,
    string? Message,
    DateTimeOffset? AcceptedAtUtc);

public sealed record EventSaleBatchResponse(EventSaleUploadResult[] Results);

public sealed record EventItemStatistic(
    string ItemId,
    string Name,
    long Quantity,
    long TotalCents);

public sealed record EventPaymentStatistic(
    string PaymentMethod,
    long SaleCount,
    long TotalCents);

public sealed record EventMemberStatistic(
    Guid MemberId,
    string Nickname,
    long SaleCount,
    long SubtotalCents,
    long TipCents,
    long TotalCents);

public sealed record EventTimelinePoint(
    DateTimeOffset StartedAtUtc,
    long SaleCount,
    long TotalCents);

public sealed record EventStatisticsResponse(
    Guid EventId,
    bool IncludesShowcase,
    long SaleCount,
    long SubtotalCents,
    long TipCents,
    long TotalCents,
    long LineCount,
    EventItemStatistic[] Items,
    EventPaymentStatistic[] PaymentMethods,
    EventMemberStatistic[] Members,
    EventTimelinePoint[] Timeline,
    DateTimeOffset CalculatedAtUtc);

public sealed record EventSaleResponse(
    string ClientSaleId,
    Guid MemberId,
    string Nickname,
    DateTimeOffset CompletedAtUtc,
    DateTimeOffset ReceivedAtUtc,
    string PaymentMethod,
    bool IsShowcase,
    long SubtotalCents,
    long TipCents,
    long TotalCents,
    long GivenCents,
    long ChangeCents,
    EventSaleLineUpload[] Lines);

public sealed record EventCloseResponse(
    EventDetailResponse Event,
    string[] UnsynchronisedNicknames);

public sealed record EventFinalizeRequest(bool ConfirmIncomplete);

public sealed record EventFinalReportResponse(
    Guid EventId,
    string EventName,
    bool IsComplete,
    string[] MissingNicknames,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    EventStatisticsResponse Statistics);

public sealed record EventRealtimeNotification(
    Guid EventId,
    string Kind,
    long Version,
    DateTimeOffset CreatedAtUtc);
