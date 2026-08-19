using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CashSloth.Contracts;
using CashSloth.Server.Data;
using CashSloth.Server.Infrastructure;
using CashSloth.Server.Security;
using CashSloth.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace CashSloth.Server;

public partial class MainWindow : Window
{
    private static readonly System.Windows.Media.Brush GoodBrush = new SolidColorBrush(Color.FromRgb(34, 197, 94));
    private static readonly System.Windows.Media.Brush WarningBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
    private static readonly System.Windows.Media.Brush BadBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38));
    private static readonly System.Windows.Media.Brush OffBrush = new SolidColorBrush(Color.FromRgb(119, 119, 119));

    private readonly ServerSettingsStore _settingsStore = new();
    private readonly ServerLogBuffer _logs = new();
    private readonly ServerCoordinator _coordinator;
    private readonly System.Windows.Forms.NotifyIcon _trayIcon;
    private bool _allowClose;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        _coordinator = new ServerCoordinator(_settingsStore, _logs);
        _coordinator.StatusChanged += OnCoordinatorStatusChanged;
        _logs.Changed += OnLogsChanged;

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "CashSloth Server",
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Öffnen", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Server starten", null, async (_, _) => await RunActionAsync(() => _coordinator.StartAsync()));
        menu.Items.Add("Server stoppen", null, async (_, _) => await RunActionAsync(() => _coordinator.StopAsync()));
        menu.Items.Add("Beenden", null, (_, _) => Dispatcher.Invoke(BeginExit));
        _trayIcon.ContextMenuStrip = menu;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await RunActionAsync(async () =>
        {
            await _coordinator.InitializeAsync();
            ApplySettingsToForm();
            await RefreshAllAsync();
            await CheckForUpdateAsync();
        }, showSuccess: false);
    }

    private async void OnStart(object sender, RoutedEventArgs e) =>
        await RunActionAsync(async () =>
        {
            await _coordinator.StartAsync();
            await RefreshAllAsync();
        }, showSuccess: false);

    private async void OnStop(object sender, RoutedEventArgs e) =>
        await RunActionAsync(async () =>
        {
            await _coordinator.StopAsync();
            await _coordinator.InitializeAsync();
            await RefreshAllAsync();
        }, showSuccess: false);

    private async void OnCheckStatus(object sender, RoutedEventArgs e) =>
        await RunActionAsync(async () =>
        {
            await _coordinator.CheckStatusAsync();
            await RefreshDashboardAsync();
        }, showSuccess: false);

    private async void OnCreateFirstAdmin(object sender, RoutedEventArgs e)
    {
        var username = FirstAdminNameBox.Text;
        var password = FirstAdminPasswordBox.Password;
        await RunActionAsync(async () =>
        {
            using var scope = _coordinator.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AccountService>().CreateFirstAdminAsync(username, password);
            FirstAdminPasswordBox.Clear();
            await RefreshAllAsync();
        }, "Der erste Administrator wurde erstellt.");
    }

    private async void OnRefreshAdministration(object sender, RoutedEventArgs e) =>
        await RunActionAsync(RefreshAdministrationAsync, showSuccess: false);

    private async void OnToggleApproval(object sender, RoutedEventArgs e)
    {
        if (AccountsGrid.SelectedItem is not AdminAccountResponse account) return;
        await WithAccountServiceAsync(service => service.SetApprovalAsync(account.Id, !account.IsApproved, "local-console"));
    }

    private async void OnSetRole(object sender, RoutedEventArgs e)
    {
        if (AccountsGrid.SelectedItem is not AdminAccountResponse account ||
            RoleComboBox.SelectedItem is not ComboBoxItem roleItem ||
            roleItem.Content is not string role) return;
        await WithAccountServiceAsync(service => service.SetRoleAsync(account.Id, role, "local-console"));
    }

    private async void OnToggleAccount(object sender, RoutedEventArgs e)
    {
        if (AccountsGrid.SelectedItem is not AdminAccountResponse account) return;
        await WithAccountServiceAsync(service => service.SetActiveAsync(account.Id, !account.IsActive, "local-console"));
    }

    private async void OnResetPassword(object sender, RoutedEventArgs e)
    {
        if (AccountsGrid.SelectedItem is not AdminAccountResponse account) return;
        await RunActionAsync(async () =>
        {
            using var scope = _coordinator.Services.CreateScope();
            var password = await scope.ServiceProvider.GetRequiredService<AccountService>()
                .ResetPasswordAsync(account.Id, "local-console");
            await RefreshAdministrationAsync();
            System.Windows.MessageBox.Show(this, $"Temporäres Passwort für {account.Username}:\n\n{password}\n\nEs muss beim nächsten Login geändert werden.", "Passwort zurückgesetzt", MessageBoxButton.OK, MessageBoxImage.Information);
        }, showSuccess: false);
    }

    private async void OnCreatePairingCode(object sender, RoutedEventArgs e) =>
        await RunActionAsync(async () =>
        {
            using var scope = _coordinator.Services.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<DevicePairingService>().CreatePairingCodeAsync();
            PairingCodeText.Text = result.Code;
            PairingExpiryText.Text = $"gültig bis {result.ExpiresAtUtc.ToLocalTime():HH:mm:ss}";
            await RefreshAdministrationAsync();
        }, showSuccess: false);

    private async void OnToggleDevice(object sender, RoutedEventArgs e)
    {
        if (DevicesGrid.SelectedItem is not AdminDeviceResponse device) return;
        await RunActionAsync(async () =>
        {
            using var scope = _coordinator.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AdministrativeQueryService>()
                .SetDeviceActiveAsync(device.Id, !device.IsActive, "local-console");
            await RefreshAdministrationAsync();
        }, showSuccess: false);
    }

    private async void OnLocalBackup(object sender, RoutedEventArgs e) =>
        await RunActionAsync(async () =>
        {
            var result = await _coordinator.Services.GetRequiredService<BackupService>().CreateLocalBackupAsync("manual");
            await RefreshDashboardAsync();
            if (result is not null)
            {
                System.Windows.MessageBox.Show(this, result.Path, "Backup erstellt", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }, showSuccess: false);

    private async void OnPortableBackup(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CashSloth-Serverbackup (*.cashsloth-server-backup)|*.cashsloth-server-backup",
            FileName = $"cashsloth-server-{DateTime.Now:yyyyMMdd}.cashsloth-server-backup"
        };
        if (dialog.ShowDialog(this) != true) return;
        var passphrase = AskPassphrase("Umzugsbackup schützen", "Gib eine Passphrase mit mindestens 12 Zeichen ein.");
        if (passphrase is null) return;
        await RunActionAsync(async () =>
        {
            var result = await _coordinator.Services.GetRequiredService<BackupService>()
                .CreatePortableBackupAsync(dialog.FileName, passphrase);
            System.Windows.MessageBox.Show(this, $"Verschlüsseltes Backup erstellt:\n{result.Path}", "Backup", MessageBoxButton.OK, MessageBoxImage.Information);
        }, showSuccess: false);
    }

    private async void OnRestoreBackup(object sender, RoutedEventArgs e)
    {
        if (_coordinator.IsRunning)
        {
            System.Windows.MessageBox.Show(this, "Stoppe den Server vor einem Restore.", "Restore", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var dialog = new OpenFileDialog { Filter = "CashSloth-Serverbackup (*.cashsloth-server-backup)|*.cashsloth-server-backup" };
        if (dialog.ShowDialog(this) != true) return;
        var passphrase = AskPassphrase("Backup wiederherstellen", "Gib die Passphrase des Backups ein.");
        if (passphrase is null) return;
        if (System.Windows.MessageBox.Show(this, "Die aktuellen Serverdaten werden nach einem zusätzlichen Sicherheitsbackup ersetzt. Fortfahren?", "Restore bestätigen", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        await RunActionAsync(async () =>
        {
            await _coordinator.PrepareForRestoreAsync();
            var settings = _settingsStore.Load();
            var paths = new ServerPaths(settings.DataPath);
            var service = new BackupService(paths, settings, _settingsStore);
            await service.RestorePortableBackupAsync(dialog.FileName, passphrase, serverIsStopped: true);
            await _coordinator.FinishRestoreAsync();
            ApplySettingsToForm();
            await RefreshAllAsync();
        }, "Backup wurde wiederhergestellt.");
    }

    private void OnBrowseCloudflared(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "cloudflared (cloudflared.exe)|cloudflared.exe|Programme (*.exe)|*.exe" };
        if (dialog.ShowDialog(this) == true) CloudflaredPathBox.Text = dialog.FileName;
    }

    private async void OnSaveSettings(object sender, RoutedEventArgs e)
    {
        var settings = _coordinator.Settings with
        {
            PublicUrl = PublicUrlBox.Text,
            DataPath = DataPathBox.Text,
            CloudflaredPath = CloudflaredPathBox.Text,
            UpdateManifestUrl = UpdateManifestBox.Text.Trim(),
            StartWithWindows = AutoStartCheckBox.IsChecked == true
        };
        await RunActionAsync(async () =>
        {
            await _coordinator.ReconfigureAsync(settings, TunnelTokenBox.Password);
            WindowsStartupService.SetEnabled(settings.StartWithWindows);
            TunnelTokenBox.Clear();
            await RefreshAllAsync();
        }, "Einstellungen wurden gespeichert.");
    }

    private void OnExportTrust(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CashSloth-Vertrauensdatei (*.cashsloth-trust)|*.cashsloth-trust",
            FileName = "cashsloth-server.cashsloth-trust"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _coordinator.Services.GetRequiredService<ServerKeyService>()
                .ExportTrustFile(dialog.FileName, _coordinator.Settings.PublicUrl);
            System.Windows.MessageBox.Show(this, "Trust-Datei wurde exportiert. Kontrolliere den Fingerprint beim Import auf jeder Kasse.", "Trust-Datei", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private void OnOpenPublicUrl(object sender, RoutedEventArgs e)
    {
        if (Uri.TryCreate(_coordinator.Settings.PublicUrl, UriKind.Absolute, out var uri))
        {
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        }
    }

    private void OnCopyPublicUrl(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_coordinator.Settings.PublicUrl))
        {
            System.Windows.Clipboard.SetText(_coordinator.Settings.PublicUrl);
        }
    }

    private async Task WithAccountServiceAsync(Func<AccountService, Task> action) =>
        await RunActionAsync(async () =>
        {
            using var scope = _coordinator.Services.CreateScope();
            await action(scope.ServiceProvider.GetRequiredService<AccountService>());
            await RefreshAdministrationAsync();
        }, showSuccess: false);

    private async Task RefreshAllAsync()
    {
        await RefreshDashboardAsync();
        await RefreshAdministrationAsync();
    }

    private async Task CheckForUpdateAsync()
    {
        var result = await UpdateCheckService.CheckOnceDailyAsync(_coordinator.Settings, _settingsStore);
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            _logs.Add("Update", result.Error);
            return;
        }
        if (!result.UpdateAvailable || result.Manifest is null)
        {
            return;
        }
        if (System.Windows.MessageBox.Show(
                this,
                $"CashSloth Server {result.Manifest.Version} ist verfügbar. Die App lädt oder installiert Updates nie automatisch. Downloadseite öffnen?",
                "Update verfügbar",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information) == MessageBoxResult.Yes)
        {
            Process.Start(new ProcessStartInfo(result.Manifest.DownloadUrl) { UseShellExecute = true });
        }
    }

    private async Task RefreshDashboardAsync()
    {
        ApplyStatus(_coordinator.Status);
        PublicUrlText.Text = _coordinator.Settings.PublicUrl;
        VersionText.Text = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0";
        DatabaseSizeText.Text = File.Exists(_coordinator.Paths.DatabasePath)
            ? FormatBytes(new FileInfo(_coordinator.Paths.DatabasePath).Length)
            : "–";

        using var scope = _coordinator.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        var lastFx = await db.ExchangeRateSnapshots.AsNoTracking()
            .OrderByDescending(value => value.Id)
            .Select(value => (DateTimeOffset?)value.FetchedAtUtc)
            .FirstOrDefaultAsync();
        LastFxText.Text = lastFx?.ToLocalTime().ToString("g") ?? "Noch nie";
        var lastBackup = _coordinator.Services.GetRequiredService<BackupService>().GetLatestLocalBackupUtc();
        LastBackupText.Text = lastBackup?.ToLocalTime().ToString("g") ?? "Noch nie";
        BackupReminderText.Text = lastBackup is null || DateTimeOffset.UtcNow - lastBackup > TimeSpan.FromDays(1)
            ? "Hinweis: Es ist wieder Zeit für ein extern aufbewahrtes Umzugsbackup."
            : $"Letztes lokales Backup: {lastBackup.Value.ToLocalTime():g}";
        FirstSetupGroup.Visibility = await db.Users.AnyAsync() ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task RefreshAdministrationAsync()
    {
        using var scope = _coordinator.Services.CreateScope();
        AccountsGrid.ItemsSource = await scope.ServiceProvider.GetRequiredService<AccountService>().ListAccountsAsync();
        var administration = scope.ServiceProvider.GetRequiredService<AdministrativeQueryService>();
        DevicesGrid.ItemsSource = await administration.ListDevicesAsync();
        AuditGrid.ItemsSource = await administration.ListAuditAsync();
    }

    private void ApplySettingsToForm()
    {
        var settings = _coordinator.Settings;
        PublicUrlBox.Text = settings.PublicUrl;
        DataPathBox.Text = settings.DataPath;
        CloudflaredPathBox.Text = settings.CloudflaredPath;
        UpdateManifestBox.Text = settings.UpdateManifestUrl;
        AutoStartCheckBox.IsChecked = settings.StartWithWindows;
    }

    private void OnCoordinatorStatusChanged(object? sender, ServerStatusSnapshot status) =>
        Dispatcher.Invoke(() => ApplyStatus(status));

    private void ApplyStatus(ServerStatusSnapshot status)
    {
        OverallStatusText.Text = status.State switch
        {
            ServerRunState.Stopped => "Gestoppt",
            ServerRunState.Starting => "Startet",
            ServerRunState.LocalOnly => "Nur lokal",
            ServerRunState.Online => "Online",
            ServerRunState.Degraded => "Beeinträchtigt",
            ServerRunState.Stopping => "Stoppt",
            _ => "Fehler"
        };
        OverallIndicator.Fill = status.State switch
        {
            ServerRunState.Online => GoodBrush,
            ServerRunState.Starting or ServerRunState.LocalOnly or ServerRunState.Degraded or ServerRunState.Stopping => WarningBrush,
            ServerRunState.Error => BadBrush,
            _ => OffBrush
        };
        ApplyBinary(LocalIndicator, LocalStatusText, status.LocalHttp, "Läuft", "Aus");
        ApplyBinary(TunnelIndicator, TunnelStatusText, status.Tunnel, "Läuft", "Aus");
        ApplyBinary(PublicIndicator, PublicStatusText, status.PublicReachability, "Ja", "Nein");
        ApplyBinary(DatabaseIndicator, DatabaseStatusText, status.Database, "Bereit", "Fehler");
        ApplyBinary(WakeIndicator, WakeStatusText, status.WakeGuard, "Aktiv", "Aus");
        LastErrorText.Text = status.LastError ?? "–";
        StartButton.IsEnabled = !_busy && !_coordinator.IsRunning;
        StopButton.IsEnabled = !_busy && _coordinator.IsRunning;
        _trayIcon.Text = $"CashSloth Server – {OverallStatusText.Text}";
    }

    private static void ApplyBinary(System.Windows.Shapes.Shape indicator, TextBlock label, bool active, string activeText, string inactiveText)
    {
        indicator.Fill = active ? GoodBrush : OffBrush;
        label.Text = active ? activeText : inactiveText;
    }

    private void OnLogsChanged(object? sender, EventArgs e) =>
        Dispatcher.Invoke(() =>
        {
            RuntimeLogBox.Text = string.Join(Environment.NewLine, _logs.Entries);
            RuntimeLogBox.ScrollToEnd();
        });

    private async Task RunActionAsync(Func<Task> action, string? successMessage = null, bool showSuccess = true)
    {
        if (_busy) return;
        _busy = true;
        ApplyStatus(_coordinator.Status);
        try
        {
            await action();
            if (showSuccess && !string.IsNullOrWhiteSpace(successMessage))
            {
                System.Windows.MessageBox.Show(this, successMessage, "CashSloth Server", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception exception)
        {
            ShowError(exception);
            try { await _coordinator.InitializeAsync(); } catch { }
        }
        finally
        {
            _busy = false;
            ApplyStatus(_coordinator.Status);
        }
    }

    private void ShowError(Exception exception) =>
        System.Windows.MessageBox.Show(this, exception.Message, "CashSloth Server", MessageBoxButton.OK, MessageBoxImage.Error);

    private string? AskPassphrase(string title, string instruction)
    {
        var dialog = new PassphraseDialog(title, instruction) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.Passphrase : null;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _coordinator.Settings.MinimizeToTray)
        {
            Hide();
            _trayIcon.ShowBalloonTip(1500, "CashSloth Server", "Die Serveroberfläche läuft im Infobereich weiter.", System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    private void RestoreFromTray() => Dispatcher.Invoke(() =>
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    });

    private void BeginExit()
    {
        _allowClose = true;
        Close();
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            await _coordinator.DisposeAsync();
            System.Windows.Application.Current.Shutdown();
            return;
        }

        if (_coordinator.IsRunning &&
            System.Windows.MessageBox.Show(this, "Der Server läuft. Soll er sauber gestoppt und die Anwendung beendet werden?", "CashSloth Server beenden", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        await RunActionAsync(() => _coordinator.StopAsync(), showSuccess: false);
        _allowClose = true;
        Close();
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824d:F2} GB",
        >= 1_048_576 => $"{bytes / 1_048_576d:F2} MB",
        >= 1024 => $"{bytes / 1024d:F1} KB",
        _ => $"{bytes} B"
    };
}
