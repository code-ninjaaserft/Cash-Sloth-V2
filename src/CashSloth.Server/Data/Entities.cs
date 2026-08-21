using Microsoft.AspNetCore.Identity;
using CashSloth.Contracts;

namespace CashSloth.Server.Data;

public sealed class ServerUser : IdentityUser
{
    public bool IsApproved { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ApprovedAtUtc { get; set; }
}

public sealed class Device
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string PublicKeyFingerprint { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? LastSeenAtUtc { get; set; }
    public ICollection<DeviceChallenge> Challenges { get; set; } = [];
    public ICollection<LoginSession> Sessions { get; set; } = [];
}

public sealed class PairingCode
{
    public Guid Id { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? UsedAtUtc { get; set; }
    public int FailedAttempts { get; set; }
}

public sealed class DeviceChallenge
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;
    public string Nonce { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? UsedAtUtc { get; set; }
}

public sealed class LoginSession
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ServerUser User { get; set; } = null!;
    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;
    public string RefreshTokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset LastRefreshedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}

public sealed class Preset
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public ICollection<PresetCategory> Categories { get; set; } = [];
    public ICollection<PresetItem> Items { get; set; } = [];
}

public sealed class PresetCategory
{
    public string PresetId { get; set; } = string.Empty;
    public Preset Preset { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class PresetItem
{
    public string PresetId { get; set; } = string.Empty;
    public Preset Preset { get; set; } = null!;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long UnitCents { get; set; }
    public string Category { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class ExchangeRateSnapshot
{
    public long Id { get; set; }
    public string BaseCurrency { get; set; } = "CHF";
    public DateOnly RateDate { get; set; }
    public DateTimeOffset FetchedAtUtc { get; set; }
    public string RatesJson { get; set; } = "{}";
}

public sealed class TranslationEntry
{
    public long Id { get; set; }
    public string SourceLanguage { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public string SourceText { get; set; } = string.Empty;
    public string SourceTextNormalized { get; set; } = string.Empty;
    public string TranslatedText { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

public sealed class AuditEvent
{
    public long Id { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string? TargetId { get; set; }
    public string? Detail { get; set; }
    public string? TraceId { get; set; }
}

public sealed class ServerMetadata
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class ServerEvent
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public CashSlothEventState State { get; set; } = CashSlothEventState.Draft;
    public string HostUserId { get; set; } = string.Empty;
    public ServerUser HostUser { get; set; } = null!;
    public string HostNickname { get; set; } = string.Empty;
    public string PresetId { get; set; } = string.Empty;
    public long PresetVersion { get; set; }
    public string PresetHash { get; set; } = string.Empty;
    public string PresetSnapshotJson { get; set; } = string.Empty;
    public CashSlothEventJoinMode JoinMode { get; set; }
    public string? JoinCodeHash { get; set; }
    public string RulesJson { get; set; } = string.Empty;
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? SalesCutoffUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public string? FinalReportJson { get; set; }
    public ICollection<EventMember> Members { get; set; } = [];
    public ICollection<EventSale> Sales { get; set; } = [];
}

public sealed class EventMember
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public ServerEvent Event { get; set; } = null!;
    public string UserId { get; set; } = string.Empty;
    public ServerUser User { get; set; } = null!;
    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;
    public CashSlothEventRole Role { get; set; }
    public CashSlothEventMemberStatus Status { get; set; } = CashSlothEventMemberStatus.Active;
    public string Nickname { get; set; } = string.Empty;
    public string NicknameNormalized { get; set; } = string.Empty;
    public DateTimeOffset JoinedAtUtc { get; set; }
    public DateTimeOffset? LastSeenAtUtc { get; set; }
    public DateTimeOffset? LeftAtUtc { get; set; }
    public DateTimeOffset? KickedAtUtc { get; set; }
    public int PendingSaleCount { get; set; }
    public DateTimeOffset? SynchronisedAtUtc { get; set; }
    public ICollection<EventSale> Sales { get; set; } = [];
}

public sealed class EventSale
{
    public string Id { get; set; } = string.Empty;
    public Guid EventId { get; set; }
    public ServerEvent Event { get; set; } = null!;
    public Guid MemberId { get; set; }
    public EventMember Member { get; set; } = null!;
    public string PayloadHash { get; set; } = string.Empty;
    public DateTimeOffset CompletedAtUtc { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public bool IsShowcase { get; set; }
    public long SubtotalCents { get; set; }
    public long TipCents { get; set; }
    public long TotalCents { get; set; }
    public long GivenCents { get; set; }
    public long ChangeCents { get; set; }
    public ICollection<EventSaleLine> Lines { get; set; } = [];
}

public sealed class EventSaleLine
{
    public string SaleId { get; set; } = string.Empty;
    public EventSale Sale { get; set; } = null!;
    public int LineIndex { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long UnitCents { get; set; }
    public int Quantity { get; set; }
    public long LineTotalCents { get; set; }
}
