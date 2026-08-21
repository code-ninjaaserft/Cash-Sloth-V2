using CashSloth.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
using System.Net.Http;

namespace CashSloth.App;

internal sealed class CashSlothEventCoordinator : IAsyncDisposable
{
    private readonly CashSlothServerClient _client;
    private readonly CashSlothServerStorage _storage;
    private readonly SaleHistorySqliteStore _history;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _runningCancellation;
    private Task? _backgroundLoop;
    private HubConnection? _hub;

    internal CashSlothEventCoordinator(
        CashSlothServerClient client,
        CashSlothServerStorage storage,
        SaleHistorySqliteStore history)
    {
        _client = client;
        _storage = storage;
        _history = history;
        Current = RestoreOfflineSession();
    }

    internal CashSlothLocalEventSession? Current { get; private set; }
    internal bool IsInEvent => Current is not null && Current.Membership.Status == CashSlothEventMemberStatus.Active &&
                               Current.Event.State is CashSlothEventState.Active or CashSlothEventState.Closing;
    internal bool CanCheckout => Current is not null &&
                                 Current.Membership.Status == CashSlothEventMemberStatus.Active &&
                                 Current.Event.State == CashSlothEventState.Active &&
                                 _client.IsEventLeaseLocallyValid(Current, out _);
    internal bool IsHost => Current?.Membership.Role == CashSlothEventRole.Host;

    internal event Action<CashSlothLocalEventSession?>? SessionChanged;
    internal event Action<string>? StatusChanged;
    internal event Action<IReadOnlyList<EventSaleUploadResult>>? SalesSynchronised;

    internal async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Current is null || _backgroundLoop is not null) return;
            _runningCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await StartHubBestEffortAsync(_runningCancellation.Token);
            _backgroundLoop = RunBackgroundLoopAsync(_runningCancellation.Token);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task ActivateAsync(
        EventDetailResponse eventDetail,
        EventMemberResponse membership,
        string lease,
        DateTimeOffset offlineUntilUtc,
        string? previousLocalPresetId,
        CancellationToken cancellationToken = default)
    {
        await StopBackgroundAsync();
        Current = new CashSlothLocalEventSession(
            eventDetail,
            membership,
            lease,
            offlineUntilUtc,
            previousLocalPresetId,
            DateTimeOffset.UtcNow);
        _storage.SaveEventSession(Current);
        SessionChanged?.Invoke(Current);
        await StartAsync(cancellationToken);
    }

    internal async Task SynchroniseNowAsync(CancellationToken cancellationToken = default)
    {
        var current = Current;
        if (current is null) return;
        if (!_history.TryListPendingEventSales(current.Event.Id, 100, out var pending, out var readError))
        {
            StatusChanged?.Invoke($"Event sync could not read the local outbox: {readError}");
            return;
        }
        if (pending.Count == 0) return;
        var matching = pending.Where(value => value.MemberId == current.Membership.Id).ToArray();
        if (matching.Length == 0) return;
        try
        {
            var uploads = matching.Select(value => ToUpload(value.Sale)).ToArray();
            var response = await _client.UploadEventSalesAsync(
                current.Event.Id,
                new EventSaleBatchRequest(current.Membership.Id, uploads),
                cancellationToken);
            if (!_history.TryApplyEventSaleSyncResults(response.Results, out var applyError))
            {
                StatusChanged?.Invoke($"Event sync response could not be stored: {applyError}");
                return;
            }
            SalesSynchronised?.Invoke(response.Results);
            var accepted = response.Results.Count(value => value.Disposition is EventSaleSyncDisposition.Accepted or EventSaleSyncDisposition.Duplicate);
            var rejected = response.Results.Length - accepted;
            StatusChanged?.Invoke(rejected == 0
                ? $"Event sync: {accepted} sale(s) synchronised."
                : $"Event sync: {accepted} synchronised, {rejected} rejected and retained locally.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or CashSlothServerException)
        {
            _history.TryMarkEventSyncAttemptFailed(matching.Select(value => value.Sale.Id), exception.Message, out _);
            StatusChanged?.Invoke($"Event offline – {matching.Length} sale(s) remain queued.");
        }
    }

    internal async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var current = Current;
        if (current is null) return;
        var detail = await _client.GetEventAsync(current.Event.Id, cancellationToken);
        var membership = detail.Members.SingleOrDefault(value => value.Id == current.Membership.Id)
            ?? current.Membership with { Status = CashSlothEventMemberStatus.Left };
        Current = current with { Event = detail, Membership = membership, SavedAtUtc = DateTimeOffset.UtcNow };
        _storage.SaveEventSession(Current);
        SessionChanged?.Invoke(Current);
    }

    internal async Task LeaveAsync(CancellationToken cancellationToken = default)
    {
        if (Current is null) return;
        await SynchroniseNowAsync(cancellationToken);
        if (!_history.TryGetPendingEventSaleCount(Current.Event.Id, out var pending, out var error))
        {
            throw new InvalidOperationException(error);
        }
        if (pending != 0)
        {
            throw new InvalidOperationException($"{pending} event sale(s) are still waiting for synchronisation.");
        }
        await _client.LeaveEventAsync(Current.Event.Id, cancellationToken);
        await ClearAsync();
    }

    internal async Task<EventCloseResponse> CloseAsync(CancellationToken cancellationToken = default)
    {
        var current = RequireCurrentHost();
        await SynchroniseNowAsync(cancellationToken);
        var response = await _client.CloseEventAsync(current.Event.Id, cancellationToken);
        Current = current with { Event = response.Event, SavedAtUtc = DateTimeOffset.UtcNow };
        _storage.SaveEventSession(Current);
        SessionChanged?.Invoke(Current);
        _history.TryGetPendingEventSaleCount(current.Event.Id, out var pending, out _);
        await _client.SendEventHeartbeatAsync(current.Event.Id, pending, cancellationToken);
        await RefreshAsync(cancellationToken);
        return response;
    }

    internal async Task<EventFinalReportResponse> FinalizeAsync(bool confirmIncomplete, CancellationToken cancellationToken = default)
    {
        var current = RequireCurrentHost();
        await SynchroniseNowAsync(cancellationToken);
        var report = await _client.FinalizeEventAsync(current.Event.Id, confirmIncomplete, cancellationToken);
        await RefreshAsync(cancellationToken);
        return report;
    }

    internal async Task RenameMemberAsync(Guid memberId, string nickname, CancellationToken cancellationToken = default)
    {
        var current = RequireCurrentHost();
        await _client.RenameEventMemberAsync(current.Event.Id, memberId, nickname, cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    internal async Task KickMemberAsync(Guid memberId, CancellationToken cancellationToken = default)
    {
        var current = RequireCurrentHost();
        await _client.KickEventMemberAsync(current.Event.Id, memberId, cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    internal async Task ClearAsync()
    {
        await StopBackgroundAsync();
        Current = null;
        _storage.ClearEventSession();
        SessionChanged?.Invoke(null);
    }

    public async ValueTask DisposeAsync()
    {
        await StopBackgroundAsync();
        _gate.Dispose();
    }

    private CashSlothLocalEventSession? RestoreOfflineSession()
    {
        var stored = _storage.LoadEventSession();
        return stored is not null && _client.IsEventLeaseLocallyValid(stored, out _) &&
               stored.Membership.Status == CashSlothEventMemberStatus.Active &&
               stored.Event.State is CashSlothEventState.Active or CashSlothEventState.Closing
            ? stored
            : null;
    }

    private async Task RunBackgroundLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            await TickAsync(cancellationToken);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await TickAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        var current = Current;
        if (current is null) return;
        try
        {
            await SynchroniseNowAsync(cancellationToken);
            _history.TryGetPendingEventSaleCount(current.Event.Id, out var pending, out _);
            var heartbeat = await _client.SendEventHeartbeatAsync(current.Event.Id, pending, cancellationToken);
            var detail = await _client.GetEventAsync(current.Event.Id, cancellationToken);
            var member = detail.Members.SingleOrDefault(value => value.Id == current.Membership.Id) ?? current.Membership;
            Current = current with
            {
                Event = detail,
                Membership = member with { Nickname = heartbeat.Nickname },
                OfflineLease = heartbeat.OfflineLease,
                OfflineUntilUtc = heartbeat.OfflineUntilUtc,
                SavedAtUtc = DateTimeOffset.UtcNow
            };
            _storage.SaveEventSession(Current);
            SessionChanged?.Invoke(Current);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or CashSlothServerException)
        {
            StatusChanged?.Invoke(_client.IsEventLeaseLocallyValid(current, out _)
                ? $"Event connection unavailable; offline checkout remains valid until {current.OfflineUntilUtc.LocalDateTime:g}."
                : "Event connection unavailable and the offline lease has expired.");
        }
    }

    private async Task StartHubBestEffortAsync(CancellationToken cancellationToken)
    {
        if (Current is null) return;
        _hub = new HubConnectionBuilder()
            .WithUrl(_client.EventHubUrl, options => options.AccessTokenProvider = () => Task.FromResult(_client.AccessToken))
            .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)])
            .Build();
        _hub.On<EventRealtimeNotification>("eventChanged", notification =>
        {
            if (Current?.Event.Id == notification.EventId)
            {
                _ = RefreshBestEffortAsync();
            }
        });
        _hub.Reconnected += async _ =>
        {
            if (Current is not null) await _hub.InvokeAsync("JoinEvent", Current.Event.Id);
        };
        try
        {
            await _hub.StartAsync(cancellationToken);
            await _hub.InvokeAsync("JoinEvent", Current.Event.Id, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            StatusChanged?.Invoke("SignalR unavailable; event updates use HTTP polling.");
            await _hub.DisposeAsync();
            _hub = null;
        }
    }

    private async Task RefreshBestEffortAsync()
    {
        try { await RefreshAsync(); } catch { }
    }

    private async Task StopBackgroundAsync()
    {
        _runningCancellation?.Cancel();
        if (_backgroundLoop is not null)
        {
            try { await _backgroundLoop; } catch (OperationCanceledException) { }
        }
        _backgroundLoop = null;
        _runningCancellation?.Dispose();
        _runningCancellation = null;
        if (_hub is not null)
        {
            using var hubStopCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await _hub.StopAsync(hubStopCancellation.Token); } catch { }
            try { await _hub.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
            _hub = null;
        }
    }

    private CashSlothLocalEventSession RequireCurrentHost()
    {
        var current = Current ?? throw new InvalidOperationException("No event is active.");
        if (current.Membership.Role != CashSlothEventRole.Host)
        {
            throw new InvalidOperationException("Only the event host can perform this action.");
        }
        return current;
    }

    private static EventSaleUpload ToUpload(SaleHistoryRecord sale) => new(
        sale.Id,
        sale.CompletedUtc,
        sale.PaymentMethod,
        sale.IsShowcase,
        sale.SubtotalCents,
        sale.TipCents,
        sale.TotalCents,
        sale.GivenCents,
        sale.ChangeCents,
        sale.Lines.Select(line => new EventSaleLineUpload(
            line.ItemId,
            line.Name,
            line.UnitCents,
            line.Quantity,
            line.LineTotalCents)).ToArray());
}
