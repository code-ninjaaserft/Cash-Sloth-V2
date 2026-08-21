using System.Diagnostics;
using System.Security.Cryptography;
using CashSloth.Server.Security;

namespace CashSloth.Server.Infrastructure;

public interface ICloudflareTunnelManager : IAsyncDisposable
{
    bool IsRunning { get; }
    event EventHandler<int>? Exited;
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public sealed class CloudflareTunnelManager(
    ServerSettings settings,
    TunnelTokenStore tokenStore,
    ServerLogBuffer logs) : ICloudflareTunnelManager
{
    private Process? _process;
    private WindowsJobObject? _job;

    public bool IsRunning => _process is { HasExited: false };
    public event EventHandler<int>? Exited;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            return;
        }
        _process?.Dispose();
        _process = null;
        _job?.Dispose();
        _job = null;
        ValidateBinary(settings.CloudflaredPath);
        var token = tokenStore.Read();
        var startInfo = new ProcessStartInfo
        {
            FileName = settings.CloudflaredPath,
            Arguments = "tunnel --no-autoupdate run",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.Environment["TUNNEL_TOKEN"] = token;

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data)) logs.Add("Tunnel", args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data)) logs.Add("Tunnel", args.Data);
        };
        process.Exited += (_, _) =>
        {
            var exitCode = process.ExitCode;
            logs.Add("Tunnel", $"cloudflared wurde mit Code {exitCode} beendet.");
            Exited?.Invoke(this, exitCode);
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("cloudflared konnte nicht gestartet werden.");
            }
            _job = new WindowsJobObject();
            _job.Add(process);
            _process = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await Task.Delay(750, cancellationToken);
            if (process.HasExited)
            {
                throw new InvalidOperationException($"cloudflared wurde sofort mit Code {process.ExitCode} beendet.");
            }
            logs.Add("Tunnel", "cloudflared gestartet; Token wurde nur über die Prozessumgebung übergeben.");
        }
        catch
        {
            process.Dispose();
            _job?.Dispose();
            _job = null;
            _process = null;
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var process = _process;
        _process = null;
        if (process is null)
        {
            _job?.Dispose();
            _job = null;
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using var stopCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                stopCancellation.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    await process.WaitForExitAsync(stopCancellation.Token);
                }
                catch (OperationCanceledException) when (stopCancellation.IsCancellationRequested)
                {
                    logs.Add("Tunnel", "cloudflared hat das 5-Sekunden-Stoppzeitlimit erreicht; das Job-Object wird geschlossen.");
                }
            }
        }
        finally
        {
            process.Dispose();
            _job?.Dispose();
            _job = null;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);

    private static void ValidateBinary(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("cloudflared.exe wurde nicht gefunden.", path);
        }
        if (!AuthenticodeVerifier.HasValidSignature(path, out var signatureError))
        {
            throw new InvalidDataException($"cloudflared.exe hat keine gültige Cloudflare-Herstellersignatur. {signatureError}");
        }

        var hashFile = path + ".sha256";
        if (!File.Exists(hashFile))
        {
            throw new InvalidDataException("Die erwartete cloudflared-SHA-256-Datei fehlt.");
        }
        var expected = File.ReadAllText(hashFile).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("cloudflared.exe stimmt nicht mit dem erwarteten Build-Hash überein.");
        }
    }
}
