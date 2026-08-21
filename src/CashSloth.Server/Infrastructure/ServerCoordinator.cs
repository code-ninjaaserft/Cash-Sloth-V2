using System.Net.Http;
using CashSloth.Contracts;
using CashSloth.Server.Api;
using CashSloth.Server.Data;
using CashSloth.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace CashSloth.Server.Infrastructure;

public enum ServerRunState
{
    Stopped,
    Starting,
    LocalOnly,
    Online,
    Degraded,
    Stopping,
    Error
}

public sealed record ServerStatusSnapshot(
    ServerRunState State,
    bool LocalHttp,
    bool Tunnel,
    bool PublicReachability,
    bool Database,
    bool WakeGuard,
    string? LastError,
    DateTimeOffset UpdatedAtUtc);

public sealed class ActiveEventsPreventStopException(IReadOnlyList<string> eventNames)
    : InvalidOperationException($"Laufende Events verhindern den normalen Server-Stopp: {string.Join(", ", eventNames)}.")
{
    public IReadOnlyList<string> EventNames { get; } = eventNames;
}

public sealed class ServerCoordinator : IAsyncDisposable
{
    private readonly ServerSettingsStore _settingsStore;
    private readonly ServerLogBuffer _logs;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HttpClient _healthClient = new() { Timeout = TimeSpan.FromSeconds(8) };
    private WebApplication? _app;
    private ICloudflareTunnelManager? _tunnel;
    private WindowsPowerGuard? _wakeGuard;
    private CancellationTokenSource? _runningCancellation;
    private Task? _backupLoop;
    private int _tunnelRetryCount;

    public ServerCoordinator(ServerSettingsStore settingsStore, ServerLogBuffer logs)
    {
        _settingsStore = settingsStore;
        _logs = logs;
        Settings = settingsStore.Load();
        Paths = new ServerPaths(Settings.DataPath);
        Status = NewStatus(ServerRunState.Stopped);
    }

    public ServerSettings Settings { get; private set; }
    public ServerPaths Paths { get; private set; }
    public ServerStatusSnapshot Status { get; private set; }
    public bool IsRunning => Status.State is ServerRunState.Starting or ServerRunState.LocalOnly or ServerRunState.Online or ServerRunState.Degraded;
    public IServiceProvider Services => _app?.Services ?? throw new InvalidOperationException("Serverdienste sind noch nicht initialisiert.");
    public event EventHandler<ServerStatusSnapshot>? StatusChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureApplicationAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning)
            {
                return;
            }
            SetStatus(NewStatus(ServerRunState.Starting));
            await EnsureApplicationAsync(cancellationToken);

            var validationError = ServerSettingsStore.Validate(Settings);
            if (validationError is not null)
            {
                throw new InvalidOperationException(validationError);
            }
            using (var scope = Services.CreateScope())
            {
                if (!await scope.ServiceProvider.GetRequiredService<ServerDbContext>().Users.AnyAsync(cancellationToken))
                {
                    throw new InvalidOperationException("Zuerst muss der erste Administrator erstellt werden.");
                }
            }

            var tokenStore = Services.GetRequiredService<Security.TunnelTokenStore>();
            if (!tokenStore.HasToken)
            {
                throw new InvalidOperationException("Es ist noch kein Cloudflare-Tunnel-Token gespeichert.");
            }

            var backup = Services.GetRequiredService<BackupService>();
            await backup.CreateLocalBackupAsync("start", cancellationToken);
            _wakeGuard = new WindowsPowerGuard();
            _wakeGuard.Activate();
            UpdateStatus(wakeGuard: true, database: true);

            await _app!.StartAsync(cancellationToken);
            if (!await CheckLocalHealthAsync(cancellationToken))
            {
                throw new InvalidOperationException("Der lokale Kestrel-Healthcheck ist fehlgeschlagen.");
            }
            SetStatus(Status with { State = ServerRunState.LocalOnly, LocalHttp = true, UpdatedAtUtc = DateTimeOffset.UtcNow });

            _tunnel = new CloudflareTunnelManager(Settings, tokenStore, _logs);
            _tunnel.Exited += OnTunnelExited;
            await _tunnel.StartAsync(cancellationToken);
            UpdateStatus(tunnel: true);

            var publicReachable = await CheckPublicHealthWithRetryAsync(cancellationToken);
            SetStatus(Status with
            {
                State = publicReachable ? ServerRunState.Online : ServerRunState.Degraded,
                PublicReachability = publicReachable,
                LastError = publicReachable ? null : "Der öffentliche Healthcheck ist noch nicht erreichbar.",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });

            _runningCancellation = new CancellationTokenSource();
            _backupLoop = RunDailyBackupLoopAsync(_runningCancellation.Token);
            _logs.Add("Server", publicReachable ? "Server ist online." : "Server läuft lokal; öffentliche Erreichbarkeit ist beeinträchtigt.");
        }
        catch (Exception exception)
        {
            _logs.Add("Server", $"Start fehlgeschlagen: {exception.Message}");
            await StopComponentsAsync(CancellationToken.None, createBackup: false);
            SetStatus(NewStatus(ServerRunState.Error, exception.Message));
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        StopAsync(emergencyStop: false, cancellationToken);

    public async Task StopAsync(bool emergencyStop, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!IsRunning && Status.State != ServerRunState.Error)
            {
                return;
            }
            var runningEvents = await GetRunningEventNamesCoreAsync(cancellationToken);
            if (runningEvents.Count > 0 && !emergencyStop)
            {
                throw new ActiveEventsPreventStopException(runningEvents);
            }
            if (runningEvents.Count > 0)
            {
                using var scope = Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<AuditService>().WriteAsync(
                    "local-console",
                    "server.emergency-stop",
                    "server",
                    detail: $"Laufende Events: {string.Join(", ", runningEvents)}",
                    cancellationToken: cancellationToken);
                _logs.Add("Server", $"Notfall-Stopp trotz laufender Events: {string.Join(", ", runningEvents)}.");
            }
            SetStatus(Status with { State = ServerRunState.Stopping, UpdatedAtUtc = DateTimeOffset.UtcNow });
            await StopComponentsAsync(cancellationToken, createBackup: true);
            SetStatus(NewStatus(ServerRunState.Stopped));
            _logs.Add("Server", "Server wurde sauber gestoppt.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> GetRunningEventNamesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await GetRunningEventNamesCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReconfigureAsync(ServerSettings settings, string? tunnelToken, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Einstellungen können nur bei gestopptem Server geändert werden.");
        }
        _settingsStore.Save(settings);
        await DisposeApplicationAsync();
        Settings = _settingsStore.Load();
        Paths = new ServerPaths(Settings.DataPath);
        await EnsureApplicationAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(tunnelToken))
        {
            Services.GetRequiredService<Security.TunnelTokenStore>().Save(tunnelToken);
        }
    }

    public async Task<ServerStatusSnapshot> CheckStatusAsync(CancellationToken cancellationToken = default)
    {
        var local = IsRunning && await CheckLocalHealthAsync(cancellationToken);
        var publicReachable = IsRunning && await CheckPublicHealthAsync(cancellationToken);
        var tunnel = _tunnel?.IsRunning == true;
        var state = !IsRunning
            ? Status.State
            : local && tunnel && publicReachable
                ? ServerRunState.Online
                : local
                    ? ServerRunState.Degraded
                    : ServerRunState.Error;
        SetStatus(Status with
        {
            State = state,
            LocalHttp = local,
            Tunnel = tunnel,
            PublicReachability = publicReachable,
            LastError = state is ServerRunState.Online or ServerRunState.Stopped ? null : Status.LastError,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        return Status;
    }

    public async Task PrepareForRestoreAsync()
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Server muss vor dem Restore gestoppt werden.");
        }
        await DisposeApplicationAsync();
    }

    public async Task FinishRestoreAsync(CancellationToken cancellationToken = default)
    {
        Settings = _settingsStore.Load();
        Paths = new ServerPaths(Settings.DataPath);
        await EnsureApplicationAsync(cancellationToken);
        SetStatus(NewStatus(ServerRunState.Stopped));
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await StopComponentsAsync(CancellationToken.None, createBackup: IsRunning);
            await DisposeApplicationAsync();
            _healthClient.Dispose();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task EnsureApplicationAsync(CancellationToken cancellationToken)
    {
        if (_app is not null)
        {
            return;
        }
        Paths.EnsureDirectories();
        _app = ServerHostFactory.Build(Settings, Paths, _settingsStore, _logs);
        _app.Services.GetRequiredService<Security.TunnelTokenStore>();
        var backup = _app.Services.GetRequiredService<BackupService>();
        await backup.CreateLocalBackupAsync("pre-migration", cancellationToken);
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        await DatabaseBootstrapper.InitializeAsync(db, cancellationToken);
        UpdateStatus(database: true);
    }

    private async Task StopComponentsAsync(CancellationToken cancellationToken, bool createBackup)
    {
        _runningCancellation?.Cancel();
        if (_backupLoop is not null)
        {
            try { await _backupLoop; } catch (OperationCanceledException) { }
        }
        _backupLoop = null;
        _runningCancellation?.Dispose();
        _runningCancellation = null;

        if (_tunnel is not null)
        {
            _tunnel.Exited -= OnTunnelExited;
            await _tunnel.StopAsync(cancellationToken);
            await _tunnel.DisposeAsync();
            _tunnel = null;
        }
        UpdateStatus(tunnel: false, publicReachability: false);

        if (_app is not null)
        {
            using var shutdownCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            shutdownCancellation.CancelAfter(TimeSpan.FromSeconds(8));
            try { await _app.StopAsync(shutdownCancellation.Token); }
            catch (InvalidOperationException) { }
            catch (OperationCanceledException) when (shutdownCancellation.IsCancellationRequested)
            {
                _logs.Add("Server", "Kestrel hat das 8-Sekunden-Stoppzeitlimit erreicht; der Host wird jetzt freigegeben.");
            }
        }
        UpdateStatus(localHttp: false);
        _wakeGuard?.Dispose();
        _wakeGuard = null;
        UpdateStatus(wakeGuard: false);

        if (createBackup && _app is not null)
        {
            await _app.Services.GetRequiredService<BackupService>().CreateLocalBackupAsync("stop", cancellationToken);
        }
        await DisposeApplicationAsync();
    }

    private async Task<IReadOnlyList<string>> GetRunningEventNamesCoreAsync(CancellationToken cancellationToken)
    {
        if (_app is null)
        {
            return [];
        }
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ServerDbContext>().Events
            .AsNoTracking()
            .Where(value => value.State == CashSlothEventState.Active || value.State == CashSlothEventState.Closing)
            .OrderBy(value => value.Name)
            .Select(value => value.Name)
            .ToListAsync(cancellationToken);
    }

    private async Task DisposeApplicationAsync()
    {
        if (_app is null)
        {
            return;
        }
        await _app.DisposeAsync();
        _app = null;
    }

    private async Task<bool> CheckLocalHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _healthClient.GetAsync("http://127.0.0.1:5080/health/live", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    private async Task<bool> CheckPublicHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            var uri = new Uri(new Uri(Settings.PublicUrl.TrimEnd('/') + "/"), "health/live");
            using var response = await _healthClient.GetAsync(uri, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return false;
        }
    }

    private async Task<bool> CheckPublicHealthWithRetryAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (await CheckPublicHealthAsync(cancellationToken))
            {
                return true;
            }
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
        return false;
    }

    private async Task RunDailyBackupLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            if (_app is null)
            {
                return;
            }
            var service = _app.Services.GetRequiredService<BackupService>();
            if (service.GetLatestLocalBackupUtc() is not { } last || DateTimeOffset.UtcNow - last >= TimeSpan.FromHours(24))
            {
                await service.CreateLocalBackupAsync("daily", cancellationToken);
            }
        }
    }

    private void OnTunnelExited(object? sender, int exitCode)
    {
        if (!IsRunning || Status.State == ServerRunState.Stopping)
        {
            return;
        }
        SetStatus(Status with
        {
            State = ServerRunState.Degraded,
            Tunnel = false,
            PublicReachability = false,
            LastError = $"Cloudflare-Tunnel wurde unerwartet beendet (Code {exitCode}).",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        _ = RetryTunnelAsync();
    }

    private async Task RetryTunnelAsync()
    {
        if (_tunnel is null || _tunnelRetryCount >= 3)
        {
            return;
        }
        var attempt = Interlocked.Increment(ref _tunnelRetryCount);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(attempt * 3), _runningCancellation?.Token ?? CancellationToken.None);
            await _tunnel.StartAsync(_runningCancellation?.Token ?? CancellationToken.None);
            var reachable = await CheckPublicHealthWithRetryAsync(_runningCancellation?.Token ?? CancellationToken.None);
            SetStatus(Status with
            {
                State = reachable ? ServerRunState.Online : ServerRunState.Degraded,
                Tunnel = true,
                PublicReachability = reachable,
                LastError = reachable ? null : "Tunnel wurde neu gestartet; öffentlicher Healthcheck ist noch nicht erreichbar.",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            if (reachable)
            {
                Interlocked.Exchange(ref _tunnelRetryCount, 0);
            }
        }
        catch (Exception exception)
        {
            _logs.Add("Tunnel", $"Neustartversuch {attempt} fehlgeschlagen: {exception.Message}");
        }
    }

    private void UpdateStatus(
        bool? localHttp = null,
        bool? tunnel = null,
        bool? publicReachability = null,
        bool? database = null,
        bool? wakeGuard = null) => SetStatus(Status with
        {
            LocalHttp = localHttp ?? Status.LocalHttp,
            Tunnel = tunnel ?? Status.Tunnel,
            PublicReachability = publicReachability ?? Status.PublicReachability,
            Database = database ?? Status.Database,
            WakeGuard = wakeGuard ?? Status.WakeGuard,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });

    private void SetStatus(ServerStatusSnapshot snapshot)
    {
        Status = snapshot;
        StatusChanged?.Invoke(this, snapshot);
    }

    private static ServerStatusSnapshot NewStatus(ServerRunState state, string? error = null) =>
        new(state, false, false, false, false, false, error, DateTimeOffset.UtcNow);
}
