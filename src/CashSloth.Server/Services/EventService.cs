using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CashSloth.Contracts;
using CashSloth.Server.Api;
using CashSloth.Server.Data;
using CashSloth.Server.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CashSloth.Server.Services;

public sealed record EventActor(string UserId, string Username, Guid DeviceId);

public sealed class EventService(
    ServerDbContext db,
    AuditService audit,
    TokenService tokens,
    IHubContext<EventHub> hub)
{
    private const int MaximumBatchSize = 100;
    private static readonly TimeSpan OnlineWindow = TimeSpan.FromSeconds(45);
    private static readonly string[] KnownPaymentMethods = ["Cash", "Card", "RFID/NFC", "TWINT", "Mobile"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly PasswordHasher<ServerEvent> CodeHasher = new();
    private const string JoinCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<IReadOnlyList<EventSummaryResponse>> ListAsync(
        EventActor actor,
        bool includeOwnedDrafts,
        CancellationToken cancellationToken)
    {
        var query = db.Events.AsNoTracking()
            .Include(value => value.HostUser)
            .Include(value => value.Members)
            .Where(value => value.State == CashSlothEventState.Active ||
                            value.State == CashSlothEventState.Closing ||
                            (includeOwnedDrafts && value.State == CashSlothEventState.Draft && value.HostUserId == actor.UserId));
        var values = await query
            .Select(value => new EventSummaryResponse(
                value.Id,
                value.Name,
                value.State,
                value.HostUser.UserName ?? string.Empty,
                value.JoinMode,
                value.Members.Count(member => member.Status == CashSlothEventMemberStatus.Active),
                value.StartedAtUtc))
            .ToListAsync(cancellationToken);
        return values.OrderByDescending(value => value.StartedAtUtc ?? DateTimeOffset.MinValue).ToArray();
    }

    public async Task<EventDetailResponse> GetAsync(Guid eventId, EventActor actor, CancellationToken cancellationToken)
    {
        var serverEvent = await LoadEventAsync(eventId, cancellationToken);
        EnsureCanView(serverEvent, actor);
        return await ToDetailAsync(serverEvent, includePreset: IsMember(serverEvent, actor) || serverEvent.HostUserId == actor.UserId, cancellationToken);
    }

    public async Task<EventDetailResponse> CreateDraftAsync(
        EventCreateRequest request,
        EventActor actor,
        CancellationToken cancellationToken)
    {
        var validated = await ValidateDraftAsync(request.Name, request.HostNickname, request.PresetId, request.PresetVersion, request.JoinMode, request.Rules, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var serverEvent = new ServerEvent
        {
            Id = Guid.NewGuid(),
            Name = validated.Name,
            State = CashSlothEventState.Draft,
            HostUserId = actor.UserId,
            HostNickname = validated.HostNickname,
            PresetId = validated.Preset.Id,
            PresetVersion = validated.Preset.Version,
            JoinMode = request.JoinMode,
            RulesJson = Serialize(validated.Rules),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Version = 1
        };
        db.Events.Add(serverEvent);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actor.Username, "event.create-draft", "event", serverEvent.Id.ToString("N"), cancellationToken: cancellationToken);
        serverEvent.HostUser = await db.Users.SingleAsync(value => value.Id == actor.UserId, cancellationToken);
        return await ToDetailAsync(serverEvent, includePreset: true, cancellationToken);
    }

    public async Task<EventDetailResponse> UpdateDraftAsync(
        Guid eventId,
        EventUpdateDraftRequest request,
        EventActor actor,
        CancellationToken cancellationToken)
    {
        var serverEvent = await LoadEventAsync(eventId, cancellationToken);
        EnsureOwner(serverEvent, actor);
        EnsureState(serverEvent, CashSlothEventState.Draft);
        if (serverEvent.Version != request.Version)
        {
            throw Problem(409, "event_version_conflict", "Der Evententwurf wurde zwischenzeitlich geändert.");
        }
        var validated = await ValidateDraftAsync(request.Name, request.HostNickname, request.PresetId, request.PresetVersion, request.JoinMode, request.Rules, cancellationToken);
        serverEvent.Name = validated.Name;
        serverEvent.HostNickname = validated.HostNickname;
        serverEvent.PresetId = validated.Preset.Id;
        serverEvent.PresetVersion = validated.Preset.Version;
        serverEvent.JoinMode = request.JoinMode;
        serverEvent.RulesJson = Serialize(validated.Rules);
        serverEvent.Version++;
        serverEvent.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actor.Username, "event.update-draft", "event", serverEvent.Id.ToString("N"), cancellationToken: cancellationToken);
        return await ToDetailAsync(serverEvent, includePreset: true, cancellationToken);
    }

    public async Task CancelDraftAsync(Guid eventId, EventActor actor, CancellationToken cancellationToken)
    {
        var serverEvent = await LoadEventAsync(eventId, cancellationToken);
        EnsureOwner(serverEvent, actor);
        EnsureState(serverEvent, CashSlothEventState.Draft);
        serverEvent.State = CashSlothEventState.Cancelled;
        serverEvent.Version++;
        serverEvent.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actor.Username, "event.cancel", "event", serverEvent.Id.ToString("N"), cancellationToken: cancellationToken);
    }

    public async Task<EventPublishResponse> PublishAsync(Guid eventId, EventActor actor, CancellationToken cancellationToken)
    {
        var serverEvent = await LoadEventAsync(eventId, cancellationToken);
        EnsureOwner(serverEvent, actor);
        EnsureState(serverEvent, CashSlothEventState.Draft);
        if (await db.Events.AnyAsync(value => value.Id != eventId && value.HostUserId == actor.UserId &&
                (value.State == CashSlothEventState.Active || value.State == CashSlothEventState.Closing), cancellationToken))
        {
            throw Problem(409, "host_already_has_active_event", "Dieser Account hostet bereits ein laufendes Event.");
        }
        await EnsureDeviceAvailableAsync(actor.DeviceId, eventId, cancellationToken);
        if (await db.Events.AnyAsync(value => value.Id != eventId &&
                (value.State == CashSlothEventState.Active || value.State == CashSlothEventState.Closing) &&
                value.Name.ToUpper() == serverEvent.Name.ToUpper(), cancellationToken))
        {
            throw Problem(409, "event_name_in_use", "Ein laufendes Event verwendet diesen Namen bereits.");
        }

        var preset = await LoadPresetDocumentAsync(serverEvent.PresetId, serverEvent.PresetVersion, cancellationToken);
        var snapshotJson = Serialize(preset with { IsActive = false });
        var rulesHash = HashText(serverEvent.RulesJson);
        var now = DateTimeOffset.UtcNow;
        var member = new EventMember
        {
            Id = Guid.NewGuid(),
            EventId = serverEvent.Id,
            UserId = actor.UserId,
            DeviceId = actor.DeviceId,
            Role = CashSlothEventRole.Host,
            Status = CashSlothEventMemberStatus.Active,
            Nickname = serverEvent.HostNickname,
            NicknameNormalized = NormalizeNickname(serverEvent.HostNickname),
            JoinedAtUtc = now,
            LastSeenAtUtc = now
        };
        string? joinCode = null;
        if (serverEvent.JoinMode == CashSlothEventJoinMode.Code)
        {
            joinCode = GenerateJoinCode();
            serverEvent.JoinCodeHash = CodeHasher.HashPassword(serverEvent, joinCode);
        }
        else
        {
            serverEvent.JoinCodeHash = null;
        }
        serverEvent.PresetSnapshotJson = snapshotJson;
        serverEvent.PresetHash = HashText(snapshotJson);
        serverEvent.State = CashSlothEventState.Active;
        serverEvent.StartedAtUtc = now;
        serverEvent.UpdatedAtUtc = now;
        serverEvent.Version++;
        member.Event = serverEvent;
        db.EventMembers.Add(member);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actor.Username, "event.publish", "event", serverEvent.Id.ToString("N"), $"Preset {preset.Id} v{preset.Version}", cancellationToken: cancellationToken);
        await NotifyAsync(serverEvent, "published", cancellationToken);
        var lease = tokens.CreateEventLease(serverEvent.Id, member.Id, actor.DeviceId, member.Role, serverEvent.PresetHash, rulesHash);
        return new EventPublishResponse(
            await ToDetailAsync(serverEvent, includePreset: true, cancellationToken),
            ToMember(member),
            lease.Token,
            lease.ExpiresAtUtc,
            joinCode);
    }

    public async Task<EventMembershipResponse> JoinAsync(
        Guid eventId,
        EventJoinRequest request,
        EventActor actor,
        CancellationToken cancellationToken)
    {
        var serverEvent = await LoadEventAsync(eventId, cancellationToken);
        EnsureState(serverEvent, CashSlothEventState.Active);
        if (serverEvent.HostUserId == actor.UserId)
        {
            throw Problem(409, "event_host_must_resume", "Der Event-Host muss die Hoststeuerung übernehmen, statt als Teilnehmer beizutreten.");
        }
        await EnsureDeviceAvailableAsync(actor.DeviceId, eventId, cancellationToken);
        VerifyJoinCode(serverEvent, request.JoinCode);

        var existing = serverEvent.Members.SingleOrDefault(value => value.UserId == actor.UserId && value.DeviceId == actor.DeviceId);
        if (existing?.Status == CashSlothEventMemberStatus.Kicked)
        {
            throw Problem(403, "event_member_kicked", "Dieses Mitglied wurde aus dem Event entfernt.");
        }
        if (existing is not null)
        {
            existing.Status = CashSlothEventMemberStatus.Active;
            existing.LeftAtUtc = null;
            existing.LastSeenAtUtc = DateTimeOffset.UtcNow;
            existing.PendingSaleCount = 0;
        }
        else
        {
            var nickname = ValidateNickname(request.Nickname);
            await EnsureNicknameAvailableAsync(serverEvent.Id, nickname, null, cancellationToken);
            existing = new EventMember
            {
                Id = Guid.NewGuid(),
                EventId = serverEvent.Id,
                UserId = actor.UserId,
                DeviceId = actor.DeviceId,
                Role = CashSlothEventRole.Participant,
                Status = CashSlothEventMemberStatus.Active,
                Nickname = nickname,
                NicknameNormalized = NormalizeNickname(nickname),
                JoinedAtUtc = DateTimeOffset.UtcNow,
                LastSeenAtUtc = DateTimeOffset.UtcNow
            };
            existing.Event = serverEvent;
            db.EventMembers.Add(existing);
        }
        serverEvent.Version++;
        serverEvent.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actor.Username, "event.join", "event-member", existing.Id.ToString("N"), $"Event {serverEvent.Id:N}; nick {existing.Nickname}", cancellationToken: cancellationToken);
        await NotifyAsync(serverEvent, "member-joined", cancellationToken);
        var lease = tokens.CreateEventLease(serverEvent.Id, existing.Id, actor.DeviceId, existing.Role, serverEvent.PresetHash, HashText(serverEvent.RulesJson));
        return new EventMembershipResponse(
            await ToDetailAsync(serverEvent, includePreset: true, cancellationToken),
            ToMember(existing),
            lease.Token,
            lease.ExpiresAtUtc);
    }

    public async Task<EventMembershipResponse> ResumeHostAsync(Guid eventId, EventActor actor, CancellationToken cancellationToken)
    {
        var serverEvent = await LoadEventAsync(eventId, cancellationToken);
        EnsureOwner(serverEvent, actor);
        if (serverEvent.State is not (CashSlothEventState.Active or CashSlothEventState.Closing))
        {
            throw Problem(409, "event_not_running", "Die Hoststeuerung kann nur für ein laufendes Event übernommen werden.");
        }
        await EnsureDeviceAvailableAsync(actor.DeviceId, eventId, cancellationToken);
        var host = serverEvent.Members.Single(value => value.Role == CashSlothEventRole.Host);
        host.DeviceId = actor.DeviceId;
        host.Status = CashSlothEventMemberStatus.Active;
        host.LeftAtUtc = null;
        host.KickedAtUtc = null;
        host.LastSeenAtUtc = DateTimeOffset.UtcNow;
        host.PendingSaleCount = 0;
        host.SynchronisedAtUtc = null;
        serverEvent.Version++;
        serverEvent.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actor.Username, "event.resume-host", "event", eventId.ToString("N"), $"Device {actor.DeviceId:N}", cancellationToken: cancellationToken);
        await NotifyAsync(serverEvent, "host-resumed", cancellationToken);
        var lease = tokens.CreateEventLease(serverEvent.Id, host.Id, actor.DeviceId, host.Role, serverEvent.PresetHash, HashText(serverEvent.RulesJson));
        return new EventMembershipResponse(
            await ToDetailAsync(serverEvent, includePreset: true, cancellationToken),
            ToMember(host),
            lease.Token,
            lease.ExpiresAtUtc);
    }

    public async Task LeaveAsync(Guid eventId, EventActor actor, CancellationToken cancellationToken)
    {
        var serverEvent = await LoadEventAsync(eventId, cancellationToken);
        var member = RequireMember(serverEvent, actor, requireActive: true);
        if (member.Role == CashSlothEventRole.Host)
        {
            throw Problem(409, "host_cannot_leave", "Der Host muss das Event beenden.");
        }
        if (member.PendingSaleCount != 0)
        {
            throw Problem(409, "pending_sales_must_sync", "Vor dem Verlassen müssen alle ausstehenden Verkäufe synchronisiert werden.");
        }
        var now = DateTimeOffset.UtcNow;
        member.Status = CashSlothEventMemberStatus.Left;
        member.LeftAtUtc = now;
        member.SynchronisedAtUtc = now;
        serverEvent.Version++;
        serverEvent.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actor.Username, "event.leave", "event-member", member.Id.ToString("N"), cancellationToken: cancellationToken);
        await NotifyAsync(serverEvent, "member-left", cancellationToken);
    }

    public async Task<EventMemberResponse> RenameMemberAsync(
        Guid eventId,
        Guid memberId,
        EventMemberRenameRequest request,
        EventActor actor,
        CancellationToken cancellationToken)
    {
        var serverEvent = await LoadEventAsync(eventId, cancellationToken);
        RequireHost(serverEvent, actor);
        var member = serverEvent.Members.SingleOrDefault(value => value.Id == memberId)
            ?? throw Problem(404, "event_member_not_found", "Eventmitglied wurde nicht gefunden.");
        var nickname = ValidateNickname(request.Nickname);
        await EnsureNicknameAvailableAsync(eventId, nickname, memberId, cancellationToken);
        var previous = member.Nickname;
        member.Nickname = nickname;
        member.NicknameNormalized = NormalizeNickname(nickname);
        if (member.Role == CashSlothEventRole.Host)
        {
            serverEvent.HostNickname = nickname;
        }
        serverEvent.Version++;
        serverEvent.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actor.Username, "event.rename-member", "event-member", member.Id.ToString("N"), $"{previous} -> {nickname}", cancellationToken: cancellationToken);
        await NotifyAsync(serverEvent, "member-renamed", cancellationToken);
        return ToMember(member);
    }

    public async Task KickMemberAsync(Guid eventId, Guid memberId, EventActor actor, CancellationToken cancellationToken)
    {
        var serverEvent = await LoadEventAsync(eventId, cancellationToken);
        RequireHost(serverEvent, actor);
        var member = serverEvent.Members.SingleOrDefault(value => value.Id == memberId)
            ?? throw Problem(404, "event_member_not_found", "Eventmitglied wurde nicht gefunden.");
        if (member.Role == CashSlothEventRole.Host)
        {
            throw Problem(409, "host_cannot_be_kicked", "Der Host kann nicht gekickt werden.");
        }
        if (member.Status == CashSlothEventMemberStatus.Kicked)
        {
            return;
        }
        var now = DateTimeOffset.UtcNow;
        member.Status = CashSlothEventMemberStatus.Kicked;
        member.KickedAtUtc = now;
        serverEvent.Version++;
        serverEvent.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actor.Username, "event.kick-member", "event-member", member.Id.ToString("N"), member.Nickname, cancellationToken: cancellationToken);
        await NotifyAsync(serverEvent, "member-kicked", cancellationToken);
    }

    public async Task<EventHeartbeatResponse> HeartbeatAsync(
        Guid eventId,
        EventHeartbeatRequest request,
        EventActor actor,
        CancellationToken cancellationToken)
    {
        var serverEvent = await LoadEventAsync(eventId, cancellationToken);
        var member = RequireMember(serverEvent, actor, requireActive: true);
        var now = DateTimeOffset.UtcNow;
        member.LastSeenAtUtc = now;
        member.PendingSaleCount = Math.Clamp(request.PendingSaleCount, 0, 100_000);
        if (serverEvent.State == CashSlothEventState.Closing && member.PendingSaleCount == 0)
        {
            member.SynchronisedAtUtc = now;
        }
        await db.SaveChangesAsync(cancellationToken);
        var lease = tokens.CreateEventLease(serverEvent.Id, member.Id, actor.DeviceId, member.Role, serverEvent.PresetHash, HashText(serverEvent.RulesJson));
        return new EventHeartbeatResponse(serverEvent.State, serverEvent.SalesCutoffUtc, member.Nickname, now, lease.Token, lease.ExpiresAtUtc);
    }

    public async Task<EventSaleBatchResponse> UploadSalesAsync(
        Guid eventId,
        EventSaleBatchRequest request,
        EventActor actor,
        CancellationToken cancellationToken)
    {
        if (request.Sales is null || request.Sales.Length is < 1 or > MaximumBatchSize)
        {
            throw Problem(400, "invalid_sale_batch", $"Ein Batch muss 1 bis {MaximumBatchSize} Verkäufe enthalten.");
        }
        var serverEvent = await LoadEventAsync(eventId, cancellationToken);
        var member = serverEvent.Members.SingleOrDefault(value => value.Id == request.MemberId && value.UserId == actor.UserId && value.DeviceId == actor.DeviceId)
            ?? throw Problem(403, "event_membership_required", "Für diesen Event besteht keine passende Mitgliedschaft.");
        var preset = Deserialize<PresetDocument>(serverEvent.PresetSnapshotJson)
            ?? throw Problem(503, "event_snapshot_invalid", "Der Event-Preset-Snapshot ist ungültig.");
        var rules = ReadRules(serverEvent);
        var results = new List<EventSaleUploadResult>(request.Sales.Length);
        var now = DateTimeOffset.UtcNow;

        foreach (var upload in request.Sales)
        {
            var id = upload.ClientSaleId?.Trim() ?? string.Empty;
            var hash = HashText(Serialize(upload));
            var existing = id.Length == 0 ? null : await db.EventSales.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
            if (existing is not null)
            {
                if (existing.EventId != eventId || existing.MemberId != member.Id || !FixedEquals(existing.PayloadHash, hash))
                {
                    results.Add(Rejected(id, "sale_id_conflict", "Die Verkaufs-ID wurde bereits mit anderen Daten verwendet."));
                }
                else
                {
                    results.Add(new EventSaleUploadResult(id, EventSaleSyncDisposition.Duplicate, null, null, existing.ReceivedAtUtc));
                }
                continue;
            }

            var validationError = ValidateSale(serverEvent, member, preset, rules, upload, now);
            if (id.Length is < 1 or > 64)
            {
                validationError = ("invalid_sale_id", "Verkaufs-ID muss 1 bis 64 Zeichen lang sein.");
            }
            if (validationError is not null)
            {
                results.Add(Rejected(id, validationError.Value.Code, validationError.Value.Message));
                continue;
            }

            var sale = new EventSale
            {
                Id = id,
                EventId = eventId,
                MemberId = member.Id,
                PayloadHash = hash,
                CompletedAtUtc = upload.CompletedAtUtc.ToUniversalTime(),
                ReceivedAtUtc = now,
                PaymentMethod = CanonicalPaymentMethod(upload.PaymentMethod),
                IsShowcase = upload.IsShowcase,
                SubtotalCents = upload.SubtotalCents,
                TipCents = upload.TipCents,
                TotalCents = upload.TotalCents,
                GivenCents = upload.GivenCents,
                ChangeCents = upload.ChangeCents
            };
            for (var index = 0; index < upload.Lines.Length; index++)
            {
                var line = upload.Lines[index];
                sale.Lines.Add(new EventSaleLine
                {
                    SaleId = id,
                    LineIndex = index,
                    ItemId = line.ItemId.Trim(),
                    Name = line.Name.Trim(),
                    UnitCents = line.UnitCents,
                    Quantity = line.Quantity,
                    LineTotalCents = line.LineTotalCents
                });
            }
            db.EventSales.Add(sale);
            results.Add(new EventSaleUploadResult(id, EventSaleSyncDisposition.Accepted, null, null, now));
        }

        member.LastSeenAtUtc = now;
        member.PendingSaleCount = Math.Max(0, member.PendingSaleCount - results.Count(value => value.Disposition is EventSaleSyncDisposition.Accepted or EventSaleSyncDisposition.Duplicate));
        if (serverEvent.State == CashSlothEventState.Closing && member.PendingSaleCount == 0)
        {
            member.SynchronisedAtUtc = now;
        }
        await db.SaveChangesAsync(cancellationToken);
        if (results.Any(value => value.Disposition == EventSaleSyncDisposition.Accepted))
        {
            await NotifyAsync(serverEvent, "sales-updated", cancellationToken);
        }
        return new EventSaleBatchResponse(results.ToArray());
    }

    public async Task<EventStatisticsResponse> GetStatisticsAsync(
        Guid eventId,
        bool includeShowcase,
        EventActor actor,
        CancellationToken cancellationToken)
    {
        var serverEvent = await LoadEventAsync(eventId, cancellationToken);
        var member = RequireMember(serverEvent, actor, requireActive: false);
        var statistics = await BuildStatisticsAsync(serverEvent, includeShowcase, cancellationToken);
        if (member.Role != CashSlothEventRole.Host)
        {
            statistics = statistics with
            {
                Items = [],
                PaymentMethods = [],
                Members = statistics.Members.Where(value => value.MemberId == member.Id).ToArray()
            };
        }
        return statistics;
    }

    public async Task<IReadOnlyList<EventSaleResponse>> GetSalesAsync(Guid eventId, EventActor actor, CancellationToken cancellationToken)
    {
        var serverEvent = await LoadEventAsync(eventId, cancellationToken);
        RequireHost(serverEvent, actor);
        return await db.EventSales.AsNoTracking()
            .Where(value => value.EventId == eventId)
            .Include(value => value.Member)
            .Include(value => value.Lines)
            .OrderByDescending(value => value.CompletedAtUtc)
            .Select(value => new EventSaleResponse(
                value.Id,
                value.MemberId,
                value.Member.Nickname,
                value.CompletedAtUtc,
                value.ReceivedAtUtc,
                value.PaymentMethod,
                value.IsShowcase,
                value.SubtotalCents,
                value.TipCents,
                value.TotalCents,
                value.GivenCents,
                value.ChangeCents,
                value.Lines.OrderBy(line => line.LineIndex).Select(line => new EventSaleLineUpload(
                    line.ItemId, line.Name, line.UnitCents, line.Quantity, line.LineTotalCents)).ToArray()))
            .ToListAsync(cancellationToken);
    }

    public async Task<EventCloseResponse> CloseAsync(Guid eventId, EventActor actor, CancellationToken cancellationToken)
    {
        var serverEvent = await LoadEventAsync(eventId, cancellationToken);
        RequireHost(serverEvent, actor);
        EnsureState(serverEvent, CashSlothEventState.Active);
        var now = DateTimeOffset.UtcNow;
        serverEvent.State = CashSlothEventState.Closing;
        serverEvent.SalesCutoffUtc = now;
        serverEvent.UpdatedAtUtc = now;
        serverEvent.Version++;
        foreach (var member in serverEvent.Members.Where(value => value.Status != CashSlothEventMemberStatus.Kicked))
        {
            member.SynchronisedAtUtc = member.Status == CashSlothEventMemberStatus.Left && member.PendingSaleCount == 0 ? now : null;
        }
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actor.Username, "event.close", "event", eventId.ToString("N"), cancellationToken: cancellationToken);
        await NotifyAsync(serverEvent, "closing", cancellationToken);
        return new EventCloseResponse(
            await ToDetailAsync(serverEvent, includePreset: true, cancellationToken),
            UnsynchronisedNicknames(serverEvent));
    }

    public async Task<EventFinalReportResponse> FinalizeAsync(
        Guid eventId,
        EventFinalizeRequest request,
        EventActor actor,
        CancellationToken cancellationToken)
    {
        var serverEvent = await LoadEventAsync(eventId, cancellationToken);
        RequireHost(serverEvent, actor);
        EnsureState(serverEvent, CashSlothEventState.Closing);
        var missing = UnsynchronisedNicknames(serverEvent);
        if (missing.Length > 0 && !request.ConfirmIncomplete)
        {
            throw Problem(409, "event_members_not_synchronised", $"Nicht synchronisiert: {string.Join(", ", missing)}.");
        }
        var ended = DateTimeOffset.UtcNow;
        var statistics = await BuildStatisticsAsync(serverEvent, includeShowcase: false, cancellationToken);
        var report = new EventFinalReportResponse(
            serverEvent.Id,
            serverEvent.Name,
            missing.Length == 0,
            missing,
            serverEvent.StartedAtUtc ?? serverEvent.CreatedAtUtc,
            ended,
            statistics);
        serverEvent.FinalReportJson = Serialize(report);
        serverEvent.State = CashSlothEventState.Ended;
        serverEvent.EndedAtUtc = ended;
        serverEvent.UpdatedAtUtc = ended;
        serverEvent.Version++;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actor.Username, "event.finalize", "event", eventId.ToString("N"), report.IsComplete ? "complete" : $"incomplete: {string.Join(", ", missing)}", cancellationToken: cancellationToken);
        await NotifyAsync(serverEvent, "ended", cancellationToken);
        return report;
    }

    public async Task<EventFinalReportResponse> GetReportAsync(Guid eventId, EventActor actor, CancellationToken cancellationToken)
    {
        var serverEvent = await LoadEventAsync(eventId, cancellationToken);
        RequireHost(serverEvent, actor, requireActiveDevice: false);
        if (serverEvent.State != CashSlothEventState.Ended || string.IsNullOrWhiteSpace(serverEvent.FinalReportJson))
        {
            throw Problem(409, "event_report_not_ready", "Der Eventbericht ist noch nicht verfügbar.");
        }
        return Deserialize<EventFinalReportResponse>(serverEvent.FinalReportJson)
            ?? throw Problem(503, "event_report_invalid", "Der gespeicherte Eventbericht ist ungültig.");
    }

    private async Task<EventStatisticsResponse> BuildStatisticsAsync(ServerEvent serverEvent, bool includeShowcase, CancellationToken cancellationToken)
    {
        var sales = await db.EventSales.AsNoTracking()
            .Where(value => value.EventId == serverEvent.Id && (includeShowcase || !value.IsShowcase))
            .Include(value => value.Member)
            .Include(value => value.Lines)
            .ToListAsync(cancellationToken);
        var items = sales.SelectMany(value => value.Lines)
            .GroupBy(value => new { value.ItemId, value.Name })
            .Select(group => new EventItemStatistic(group.Key.ItemId, group.Key.Name, group.Sum(value => (long)value.Quantity), group.Sum(value => value.LineTotalCents)))
            .OrderByDescending(value => value.TotalCents)
            .ToArray();
        var payments = sales.GroupBy(value => value.PaymentMethod, StringComparer.OrdinalIgnoreCase)
            .Select(group => new EventPaymentStatistic(group.Key, group.LongCount(), group.Sum(value => value.TotalCents)))
            .OrderByDescending(value => value.TotalCents)
            .ToArray();
        var members = sales.GroupBy(value => new { value.MemberId, value.Member.Nickname })
            .Select(group => new EventMemberStatistic(
                group.Key.MemberId,
                group.Key.Nickname,
                group.LongCount(),
                group.Sum(value => value.SubtotalCents),
                group.Sum(value => value.TipCents),
                group.Sum(value => value.TotalCents)))
            .OrderBy(value => value.Nickname)
            .ToArray();
        var timeline = sales.GroupBy(value => new DateTimeOffset(
                value.CompletedAtUtc.Year,
                value.CompletedAtUtc.Month,
                value.CompletedAtUtc.Day,
                value.CompletedAtUtc.Hour,
                0,
                0,
                TimeSpan.Zero))
            .Select(group => new EventTimelinePoint(group.Key, group.LongCount(), group.Sum(value => value.TotalCents)))
            .OrderBy(value => value.StartedAtUtc)
            .ToArray();
        return new EventStatisticsResponse(
            serverEvent.Id,
            includeShowcase,
            sales.LongCount(),
            sales.Sum(value => value.SubtotalCents),
            sales.Sum(value => value.TipCents),
            sales.Sum(value => value.TotalCents),
            sales.Sum(value => (long)value.Lines.Count),
            items,
            payments,
            members,
            timeline,
            DateTimeOffset.UtcNow);
    }

    private async Task<ServerEvent> LoadEventAsync(Guid eventId, CancellationToken cancellationToken) =>
        await db.Events
            .Include(value => value.HostUser)
            .Include(value => value.Members).ThenInclude(value => value.User)
            .Include(value => value.Members).ThenInclude(value => value.Device)
            .SingleOrDefaultAsync(value => value.Id == eventId, cancellationToken)
        ?? throw Problem(404, "event_not_found", "Event wurde nicht gefunden.");

    private async Task<EventDetailResponse> ToDetailAsync(ServerEvent serverEvent, bool includePreset, CancellationToken cancellationToken)
    {
        PresetDocument? preset = null;
        if (includePreset)
        {
            preset = string.IsNullOrWhiteSpace(serverEvent.PresetSnapshotJson)
                ? await LoadPresetDocumentAsync(serverEvent.PresetId, serverEvent.PresetVersion, cancellationToken)
                : Deserialize<PresetDocument>(serverEvent.PresetSnapshotJson);
        }
        return new EventDetailResponse(
            serverEvent.Id,
            serverEvent.Name,
            serverEvent.State,
            serverEvent.HostUserId,
            serverEvent.HostUser.UserName ?? string.Empty,
            serverEvent.HostNickname,
            serverEvent.PresetId,
            serverEvent.PresetVersion,
            serverEvent.PresetHash,
            preset,
            serverEvent.JoinMode,
            ReadRules(serverEvent),
            serverEvent.Version,
            serverEvent.CreatedAtUtc,
            serverEvent.StartedAtUtc,
            serverEvent.SalesCutoffUtc,
            serverEvent.EndedAtUtc,
            serverEvent.Members.OrderBy(value => value.Role == CashSlothEventRole.Host ? 0 : 1).ThenBy(value => value.Nickname).Select(ToMember).ToArray());
    }

    private static EventMemberResponse ToMember(EventMember member)
    {
        var online = member.Status == CashSlothEventMemberStatus.Active &&
                     member.LastSeenAtUtc >= DateTimeOffset.UtcNow.Subtract(OnlineWindow);
        return new EventMemberResponse(
            member.Id,
            member.UserId,
            member.DeviceId,
            member.Role,
            member.Status,
            member.Nickname,
            online,
            member.JoinedAtUtc,
            member.LastSeenAtUtc,
            member.LeftAtUtc,
            member.KickedAtUtc,
            member.PendingSaleCount);
    }

    private async Task<(string Name, string HostNickname, PresetDocument Preset, EventRulesDocument Rules)> ValidateDraftAsync(
        string name,
        string hostNickname,
        string presetId,
        long presetVersion,
        CashSlothEventJoinMode joinMode,
        EventRulesDocument rules,
        CancellationToken cancellationToken)
    {
        var normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length is < 1 or > 120)
        {
            throw Problem(400, "invalid_event_name", "Eventname muss 1 bis 120 Zeichen lang sein.");
        }
        if (!Enum.IsDefined(joinMode))
        {
            throw Problem(400, "invalid_event_join_mode", "Unbekannte Beitrittsart.");
        }
        var nickname = ValidateNickname(hostNickname);
        var validatedRules = ValidateRules(rules);
        var preset = await LoadPresetDocumentAsync(presetId, presetVersion, cancellationToken);
        return (normalizedName, nickname, preset, validatedRules);
    }

    private static EventRulesDocument ValidateRules(EventRulesDocument? rules)
    {
        if (rules?.AllowedPaymentMethods is not { Length: > 0 })
        {
            throw Problem(400, "event_payment_methods_required", "Mindestens eine Zahlungsmethode ist erforderlich.");
        }
        var methods = rules.AllowedPaymentMethods
            .Select(CanonicalPaymentMethod)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (methods.Any(value => value.Length == 0))
        {
            throw Problem(400, "invalid_event_payment_method", "Eine Zahlungsmethode ist unbekannt.");
        }
        return new EventRulesDocument(methods, rules.AllowTips, rules.AllowShowcase);
    }

    private async Task<PresetDocument> LoadPresetDocumentAsync(string presetId, long version, CancellationToken cancellationToken)
    {
        var normalized = PresetService.NormalizeId(presetId);
        var preset = await db.Presets.AsNoTracking()
            .Include(value => value.Categories)
            .Include(value => value.Items)
            .SingleOrDefaultAsync(value => value.Id == normalized, cancellationToken)
            ?? throw Problem(404, "preset_not_found", "Das gewählte zentrale Preset wurde nicht gefunden.");
        if (version <= 0 || preset.Version != version)
        {
            throw Problem(409, "preset_version_conflict", "Die gewählte Presetversion ist nicht mehr aktuell.");
        }
        return new PresetDocument(
            preset.Id,
            preset.Name,
            preset.Categories.OrderBy(value => value.SortOrder).Select(value => value.Name).ToArray(),
            preset.Items.OrderBy(value => value.SortOrder).Select(value => new PresetItemDocument(value.Id, value.Name, value.UnitCents, value.Category)).ToArray(),
            preset.Version,
            false,
            preset.UpdatedAtUtc);
    }

    private async Task EnsureDeviceAvailableAsync(Guid deviceId, Guid currentEventId, CancellationToken cancellationToken)
    {
        var occupied = await db.EventMembers.AsNoTracking().AnyAsync(member =>
            member.DeviceId == deviceId &&
            member.EventId != currentEventId &&
            member.Status == CashSlothEventMemberStatus.Active &&
            (member.Event.State == CashSlothEventState.Active || member.Event.State == CashSlothEventState.Closing),
            cancellationToken);
        if (occupied)
        {
            throw Problem(409, "device_already_in_event", "Dieses Gerät ist bereits in einem laufenden Event.");
        }
    }

    private async Task EnsureNicknameAvailableAsync(Guid eventId, string nickname, Guid? exceptMemberId, CancellationToken cancellationToken)
    {
        var normalized = NormalizeNickname(nickname);
        if (await db.EventMembers.AsNoTracking().AnyAsync(value => value.EventId == eventId && value.NicknameNormalized == normalized && value.Id != exceptMemberId, cancellationToken))
        {
            throw Problem(409, "event_nickname_in_use", "Dieser Event-Nick ist bereits reserviert.");
        }
    }

    private static (string Code, string Message)? ValidateSale(
        ServerEvent serverEvent,
        EventMember member,
        PresetDocument preset,
        EventRulesDocument rules,
        EventSaleUpload upload,
        DateTimeOffset now)
    {
        if (serverEvent.State is CashSlothEventState.Draft or CashSlothEventState.Cancelled or CashSlothEventState.Ended)
        {
            return ("event_not_accepting_sales", "Event nimmt keine Verkäufe mehr an.");
        }
        var cutoff = new[] { serverEvent.SalesCutoffUtc, member.KickedAtUtc, member.LeftAtUtc }
            .Where(value => value.HasValue).Select(value => value!.Value).DefaultIfEmpty(DateTimeOffset.MaxValue).Min();
        if (upload.CompletedAtUtc > cutoff)
        {
            return ("sale_after_cutoff", "Verkauf wurde nach dem Event-/Mitglieds-Cutoff abgeschlossen.");
        }
        var eventStart = serverEvent.StartedAtUtc ?? serverEvent.CreatedAtUtc;
        var memberStart = member.JoinedAtUtc > eventStart ? member.JoinedAtUtc : eventStart;
        if (upload.CompletedAtUtc > now.AddMinutes(5) || upload.CompletedAtUtc < memberStart.AddMinutes(-5))
        {
            return ("invalid_sale_time", "Verkaufszeit liegt ausserhalb des zulässigen Eventzeitraums.");
        }
        var payment = CanonicalPaymentMethod(upload.PaymentMethod);
        if (payment.Length == 0 || !rules.AllowedPaymentMethods.Contains(payment, StringComparer.OrdinalIgnoreCase))
        {
            return ("payment_method_not_allowed", "Zahlungsmethode ist für dieses Event nicht erlaubt.");
        }
        if (!rules.AllowTips && upload.TipCents != 0)
        {
            return ("tips_not_allowed", "Trinkgeld ist für dieses Event deaktiviert.");
        }
        if (!rules.AllowShowcase && upload.IsShowcase)
        {
            return ("showcase_not_allowed", "Showcase-Verkäufe sind für dieses Event deaktiviert.");
        }
        if (upload.Lines is not { Length: > 0 } || upload.Lines.Length > 500 || upload.SubtotalCents <= 0 || upload.TipCents < 0 || upload.TotalCents <= 0)
        {
            return ("invalid_sale", "Verkauf enthält ungültige Beträge oder Positionen.");
        }
        var items = preset.Items.ToDictionary(value => value.Id, StringComparer.Ordinal);
        long subtotal = 0;
        foreach (var line in upload.Lines)
        {
            if (!items.TryGetValue(line.ItemId?.Trim() ?? string.Empty, out var item) ||
                line.Quantity is < 1 or > 100_000 ||
                line.UnitCents != item.UnitCents ||
                line.LineTotalCents != item.UnitCents * line.Quantity)
            {
                return ("sale_line_mismatch", "Eine Verkaufsposition passt nicht zum Event-Preset.");
            }
            try { subtotal = checked(subtotal + line.LineTotalCents); }
            catch (OverflowException) { return ("sale_total_overflow", "Verkaufssumme ist zu gross."); }
        }
        if (subtotal != upload.SubtotalCents || upload.TotalCents != upload.SubtotalCents + upload.TipCents ||
            upload.GivenCents < upload.TotalCents || upload.ChangeCents != upload.GivenCents - upload.TotalCents)
        {
            return ("sale_total_mismatch", "Verkaufssummen sind nicht konsistent.");
        }
        return null;
    }

    private static EventSaleUploadResult Rejected(string id, string code, string message) =>
        new(id, EventSaleSyncDisposition.Rejected, code, message, null);

    private static void EnsureCanView(ServerEvent serverEvent, EventActor actor)
    {
        if (serverEvent.HostUserId != actor.UserId && !IsMember(serverEvent, actor))
        {
            throw Problem(404, "event_not_found", "Event wurde nicht gefunden.");
        }
    }

    private static bool IsMember(ServerEvent serverEvent, EventActor actor) =>
        serverEvent.Members.Any(value => value.UserId == actor.UserId && value.DeviceId == actor.DeviceId);

    private static EventMember RequireMember(ServerEvent serverEvent, EventActor actor, bool requireActive)
    {
        var member = serverEvent.Members.SingleOrDefault(value => value.UserId == actor.UserId && value.DeviceId == actor.DeviceId)
            ?? throw Problem(403, "event_membership_required", "Für dieses Event besteht keine Mitgliedschaft.");
        if (requireActive && member.Status != CashSlothEventMemberStatus.Active)
        {
            throw Problem(403, member.Status == CashSlothEventMemberStatus.Kicked ? "event_member_kicked" : "event_member_inactive", "Die Eventmitgliedschaft ist nicht aktiv.");
        }
        return member;
    }

    private static EventMember RequireHost(ServerEvent serverEvent, EventActor actor, bool requireActiveDevice = true)
    {
        EnsureOwner(serverEvent, actor);
        var host = serverEvent.Members.SingleOrDefault(value => value.Role == CashSlothEventRole.Host)
            ?? throw Problem(409, "event_host_missing", "Event besitzt keine Hostmitgliedschaft.");
        if (requireActiveDevice && (host.DeviceId != actor.DeviceId || host.Status != CashSlothEventMemberStatus.Active))
        {
            throw Problem(403, "event_host_control_required", "Die Hoststeuerung ist auf diesem Gerät nicht aktiv.");
        }
        return host;
    }

    private static void EnsureOwner(ServerEvent serverEvent, EventActor actor)
    {
        if (serverEvent.HostUserId != actor.UserId)
        {
            throw Problem(403, "event_host_required", "Nur der Event-Host darf diese Aktion ausführen.");
        }
    }

    private static void EnsureState(ServerEvent serverEvent, CashSlothEventState expected)
    {
        if (serverEvent.State != expected)
        {
            throw Problem(409, "event_state_conflict", $"Event befindet sich in Zustand {serverEvent.State} statt {expected}.");
        }
    }

    private static void VerifyJoinCode(ServerEvent serverEvent, string? code)
    {
        if (serverEvent.JoinMode != CashSlothEventJoinMode.Code)
        {
            return;
        }
        var normalized = NormalizeJoinCode(code);
        if (string.IsNullOrWhiteSpace(serverEvent.JoinCodeHash) ||
            CodeHasher.VerifyHashedPassword(serverEvent, serverEvent.JoinCodeHash, normalized) == PasswordVerificationResult.Failed)
        {
            throw Problem(403, "invalid_event_join_code", "Event-Code ist ungültig.");
        }
    }

    private static string[] UnsynchronisedNicknames(ServerEvent serverEvent) =>
        serverEvent.Members
            .Where(value => value.Status != CashSlothEventMemberStatus.Kicked && value.SynchronisedAtUtc is null)
            .Select(value => value.Nickname)
            .OrderBy(value => value)
            .ToArray();

    private async Task NotifyAsync(ServerEvent serverEvent, string kind, CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.Group(EventHub.GroupName(serverEvent.Id)).SendAsync(
                "eventChanged",
                new EventRealtimeNotification(serverEvent.Id, kind, serverEvent.Version, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch
        {
            // HTTP/polling remains the source of truth; a realtime hint must never roll back a committed mutation.
        }
    }

    private static string ValidateNickname(string? value)
    {
        var nickname = value?.Trim() ?? string.Empty;
        if (nickname.Length is < 1 or > 40 || nickname.Any(char.IsControl))
        {
            throw Problem(400, "invalid_event_nickname", "Event-Nick muss 1 bis 40 druckbare Zeichen lang sein.");
        }
        return nickname;
    }

    private static string NormalizeNickname(string value) => value.Trim().ToUpperInvariant();
    private static string NormalizeJoinCode(string? value) => new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string GenerateJoinCode()
    {
        Span<char> code = stackalloc char[8];
        for (var index = 0; index < code.Length; index++)
        {
            code[index] = JoinCodeAlphabet[RandomNumberGenerator.GetInt32(JoinCodeAlphabet.Length)];
        }
        return new string(code);
    }

    private static string CanonicalPaymentMethod(string? value)
    {
        var method = value?.Trim() ?? string.Empty;
        return KnownPaymentMethods.FirstOrDefault(known => string.Equals(known, method, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
    }

    private static EventRulesDocument ReadRules(ServerEvent serverEvent) =>
        Deserialize<EventRulesDocument>(serverEvent.RulesJson)
        ?? throw Problem(503, "event_rules_invalid", "Gespeicherte Eventregeln sind ungültig.");

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
    private static T? Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, JsonOptions);
    private static string HashText(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool FixedEquals(string left, string right) =>
        left.Length == right.Length && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));

    private static ApiProblemException Problem(int status, string code, string message) => new(status, code, message);
}
