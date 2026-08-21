using CashSloth.Contracts;
using CashSloth.Server.Api;
using CashSloth.Server.Data;
using CashSloth.Server.Services;
using Microsoft.AspNetCore.Identity;

namespace CashSloth.Server.Tests;

public sealed class EventServiceTests
{
    [Fact]
    public async Task EventLifecycle_FreezesPreset_SynchronisesSales_AndBuildsFinalReport()
    {
        await using var context = await TestServerContext.CreateAsync();
        using var scope = context.CreateScope();
        var services = scope.ServiceProvider;
        var host = await CreateActorAsync(services, "host");
        var participant = await CreateActorAsync(services, "cashier");
        var presets = services.GetRequiredService<PresetService>();
        var createdPreset = await presets.CreateAsync(
            PresetServiceTests.CreatePreset("EVENT", "Event assortment"),
            "test",
            CancellationToken.None);
        var events = services.GetRequiredService<EventService>();

        var draft = await events.CreateDraftAsync(new EventCreateRequest(
            "Sommerfest",
            "Kasse 1",
            "EVENT",
            createdPreset.Version,
            CashSlothEventJoinMode.Code,
            new EventRulesDocument(["Cash", "TWINT"], AllowTips: true, AllowShowcase: false)), host, CancellationToken.None);
        Assert.Equal(CashSlothEventState.Draft, draft.State);

        var originalVersion = draft.Version;
        draft = await events.UpdateDraftAsync(draft.Id, new EventUpdateDraftRequest(
            "Sommerfest 2026", "Kasse 1", "EVENT", createdPreset.Version, CashSlothEventJoinMode.Code,
            draft.Rules, draft.Version), host, CancellationToken.None);
        var staleDraft = await Assert.ThrowsAsync<ApiProblemException>(() =>
            events.UpdateDraftAsync(draft.Id, new EventUpdateDraftRequest(
                "Stale", "Kasse 1", "EVENT", createdPreset.Version, CashSlothEventJoinMode.Code,
                draft.Rules, originalVersion), host, CancellationToken.None));
        Assert.Equal("event_version_conflict", staleDraft.Code);

        var published = await events.PublishAsync(draft.Id, host, CancellationToken.None);
        Assert.Equal(CashSlothEventState.Active, published.Event.State);
        Assert.NotNull(published.Event.Preset);
        Assert.Equal(8, published.JoinCode?.Length);
        Assert.False(string.IsNullOrWhiteSpace(published.OfflineLease));

        var invalidCode = await Assert.ThrowsAsync<ApiProblemException>(() =>
            events.JoinAsync(draft.Id, new EventJoinRequest("Kasse 2", "WRONG"), participant, CancellationToken.None));
        Assert.Equal("invalid_event_join_code", invalidCode.Code);

        var joined = await events.JoinAsync(
            draft.Id,
            new EventJoinRequest("Kasse 2", published.JoinCode),
            participant,
            CancellationToken.None);
        Assert.Equal(CashSlothEventRole.Participant, joined.Membership.Role);

        var completedAt = DateTimeOffset.UtcNow;
        var upload = new EventSaleUpload(
            "sale-1",
            completedAt,
            "Cash",
            false,
            450,
            50,
            500,
            500,
            0,
            [new EventSaleLineUpload("COFFEE", "Coffee", 450, 1, 450)]);
        var firstBatch = await events.UploadSalesAsync(
            draft.Id,
            new EventSaleBatchRequest(joined.Membership.Id, [upload]),
            participant,
            CancellationToken.None);
        Assert.Equal(EventSaleSyncDisposition.Accepted, Assert.Single(firstBatch.Results).Disposition);

        var duplicateBatch = await events.UploadSalesAsync(
            draft.Id,
            new EventSaleBatchRequest(joined.Membership.Id, [upload]),
            participant,
            CancellationToken.None);
        Assert.Equal(EventSaleSyncDisposition.Duplicate, Assert.Single(duplicateBatch.Results).Disposition);

        var participantStats = await events.GetStatisticsAsync(draft.Id, false, participant, CancellationToken.None);
        Assert.Empty(participantStats.Items);
        Assert.Empty(participantStats.PaymentMethods);
        Assert.Single(participantStats.Members);

        var closing = await events.CloseAsync(draft.Id, host, CancellationToken.None);
        Assert.Equal(CashSlothEventState.Closing, closing.Event.State);
        var lateSale = upload with { ClientSaleId = "sale-late", CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(1) };
        var rejected = await events.UploadSalesAsync(
            draft.Id,
            new EventSaleBatchRequest(joined.Membership.Id, [lateSale]),
            participant,
            CancellationToken.None);
        Assert.Equal("sale_after_cutoff", Assert.Single(rejected.Results).ErrorCode);

        await events.HeartbeatAsync(draft.Id, new EventHeartbeatRequest(0), host, CancellationToken.None);
        await events.HeartbeatAsync(draft.Id, new EventHeartbeatRequest(0), participant, CancellationToken.None);
        var report = await events.FinalizeAsync(
            draft.Id,
            new EventFinalizeRequest(ConfirmIncomplete: false),
            host,
            CancellationToken.None);
        Assert.True(report.IsComplete);
        Assert.Equal(1, report.Statistics.SaleCount);
        Assert.Equal(500, report.Statistics.TotalCents);
        Assert.Equal(CashSlothEventState.Ended, (await events.GetAsync(draft.Id, host, CancellationToken.None)).State);
    }

    [Fact]
    public async Task EventNickname_IsReserved_AndKickIsPermanent()
    {
        await using var context = await TestServerContext.CreateAsync();
        using var scope = context.CreateScope();
        var services = scope.ServiceProvider;
        var host = await CreateActorAsync(services, "host");
        var first = await CreateActorAsync(services, "first");
        var second = await CreateActorAsync(services, "second");
        var preset = await services.GetRequiredService<PresetService>().CreateAsync(
            PresetServiceTests.CreatePreset("EVENT", "Event assortment"), "test", CancellationToken.None);
        var events = services.GetRequiredService<EventService>();
        var draft = await events.CreateDraftAsync(new EventCreateRequest(
            "Night market", "Host", "EVENT", preset.Version, CashSlothEventJoinMode.Open,
            new EventRulesDocument(["Cash"], false, false)), host, CancellationToken.None);
        var published = await events.PublishAsync(draft.Id, host, CancellationToken.None);
        var joined = await events.JoinAsync(draft.Id, new EventJoinRequest("Kasse 2", null), first, CancellationToken.None);

        await events.LeaveAsync(draft.Id, first, CancellationToken.None);
        var reserved = await Assert.ThrowsAsync<ApiProblemException>(() =>
            events.JoinAsync(draft.Id, new EventJoinRequest("Kasse 2", null), second, CancellationToken.None));
        Assert.Equal("event_nickname_in_use", reserved.Code);

        var rejoined = await events.JoinAsync(draft.Id, new EventJoinRequest("ignored", null), first, CancellationToken.None);
        Assert.Equal("Kasse 2", rejoined.Membership.Nickname);
        await events.RenameMemberAsync(draft.Id, joined.Membership.Id, new EventMemberRenameRequest("Bar"), host, CancellationToken.None);
        await events.KickMemberAsync(draft.Id, joined.Membership.Id, host, CancellationToken.None);
        var kicked = await Assert.ThrowsAsync<ApiProblemException>(() =>
            events.JoinAsync(draft.Id, new EventJoinRequest("Bar", null), first, CancellationToken.None));
        Assert.Equal("event_member_kicked", kicked.Code);
        Assert.Equal(CashSlothEventState.Active, published.Event.State);
    }

    private static async Task<EventActor> CreateActorAsync(IServiceProvider services, string username)
    {
        var manager = services.GetRequiredService<UserManager<ServerUser>>();
        var user = new ServerUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = username,
            IsApproved = true,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            LockoutEnabled = true
        };
        Assert.True((await manager.CreateAsync(user, "Very-Strong-Password-42!")).Succeeded);
        var device = new Device
        {
            Id = Guid.NewGuid(),
            Name = username,
            PublicKey = Convert.ToBase64String([1, 2, 3]),
            PublicKeyFingerprint = Guid.NewGuid().ToString("N"),
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        var db = services.GetRequiredService<ServerDbContext>();
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        return new EventActor(user.Id, username, device.Id);
    }
}
