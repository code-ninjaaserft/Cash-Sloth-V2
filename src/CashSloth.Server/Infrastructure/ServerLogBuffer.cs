using System.Collections.ObjectModel;

namespace CashSloth.Server.Infrastructure;

public sealed class ServerLogBuffer
{
    private readonly object _gate = new();
    private readonly Queue<string> _entries = new();

    public event EventHandler? Changed;

    public IReadOnlyList<string> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToArray();
            }
        }
    }

    public void Add(string component, string message)
    {
        var sanitized = Sanitize(message);
        lock (_gate)
        {
            _entries.Enqueue($"{DateTimeOffset.Now:HH:mm:ss} [{component}] {sanitized}");
            while (_entries.Count > 500)
            {
                _entries.Dequeue();
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string Sanitize(string value)
    {
        var line = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        foreach (var marker in new[] { "TUNNEL_TOKEN=", "Authorization:", "refreshToken", "password" })
        {
            var index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                line = line[..index] + marker + " [REDACTED]";
            }
        }
        return line.Length <= 1000 ? line : line[..1000];
    }
}
