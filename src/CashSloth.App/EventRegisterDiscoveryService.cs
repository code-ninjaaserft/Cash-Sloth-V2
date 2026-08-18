using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CashSloth.App;

internal sealed record EventRegisterAdvertisement(
    string Id,
    string EventName,
    string RegisterName,
    string HostName,
    string Endpoint,
    DateTimeOffset LastSeenUtc)
{
    public override string ToString()
    {
        return $"{RegisterName} @ {HostName} ({Endpoint})";
    }
}

internal sealed class EventRegisterDiscoveryService : IDisposable
{
    private const int DiscoveryPort = 43782;
    private const string DiscoverMessageType = "cashsloth.event.discover.v1";
    private const string AdvertisementMessageType = "cashsloth.event.register.v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private UdpClient? _advertiseClient;
    private CancellationTokenSource? _advertiseCancellation;
    private Task? _advertiseTask;
    private string _advertisedRegisterId = string.Empty;
    private string _advertisedEventName = string.Empty;
    private string _advertisedRegisterName = string.Empty;

    internal bool IsAdvertising => _advertiseClient != null;

    internal async Task<IReadOnlyList<EventRegisterAdvertisement>> ScanAsync(string eventName, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var client = new UdpClient(0)
        {
            EnableBroadcast = true
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var requestId = Guid.NewGuid().ToString("N");
        var request = JsonSerializer.Serialize(new DiscoveryMessage(
            DiscoverMessageType,
            requestId,
            NormalizeRequiredText(eventName, "Default Event")));
        var requestBytes = Encoding.UTF8.GetBytes(request);
        await client.SendAsync(requestBytes, requestBytes.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));

        var found = new Dictionary<string, EventRegisterAdvertisement>(StringComparer.OrdinalIgnoreCase);
        while (!timeoutCts.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await client.ReceiveAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                continue;
            }

            var advertisement = TryParseAdvertisement(result, requestId);
            if (advertisement == null)
            {
                continue;
            }

            found[advertisement.Id] = advertisement;
        }

        return found.Values
            .OrderBy(register => register.RegisterName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal bool TryStartAdvertising(string eventName, string registerName, out string? error)
    {
        error = null;
        if (IsAdvertising)
        {
            return true;
        }

        _advertisedRegisterId = BuildStableRegisterId(eventName, registerName);
        _advertisedEventName = NormalizeRequiredText(eventName, "Default Event");
        _advertisedRegisterName = NormalizeRequiredText(registerName, "Kasse");

        try
        {
            _advertiseCancellation = new CancellationTokenSource();
            _advertiseClient = new UdpClient
            {
                ExclusiveAddressUse = false
            };
            _advertiseClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _advertiseClient.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            _advertiseTask = RunAdvertisementLoopAsync(_advertiseClient, _advertiseCancellation.Token);
            return true;
        }
        catch (Exception ex)
        {
            StopAdvertising();
            error = ex.Message;
            return false;
        }
    }

    internal void StopAdvertising()
    {
        _advertiseCancellation?.Cancel();
        _advertiseClient?.Dispose();
        _advertiseClient = null;
        _advertiseCancellation?.Dispose();
        _advertiseCancellation = null;
        _advertiseTask = null;
        _advertisedRegisterId = string.Empty;
        _advertisedEventName = string.Empty;
        _advertisedRegisterName = string.Empty;
    }

    public void Dispose()
    {
        StopAdvertising();
    }

    private async Task RunAdvertisementLoopAsync(UdpClient client, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await client.ReceiveAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
                continue;
            }

            var request = TryParseDiscovery(result);
            if (request == null)
            {
                continue;
            }

            if (!string.Equals(request.EventName, _advertisedEventName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var response = JsonSerializer.Serialize(new AdvertisementMessage(
                AdvertisementMessageType,
                request.RequestId,
                _advertisedRegisterId,
                _advertisedEventName,
                _advertisedRegisterName,
                Environment.MachineName));
            var responseBytes = Encoding.UTF8.GetBytes(response);

            try
            {
                await client.SendAsync(responseBytes, responseBytes.Length, result.RemoteEndPoint);
            }
            catch
            {
                // Discovery is best-effort; the UI can retry scans.
            }
        }
    }

    private static DiscoveryMessage? TryParseDiscovery(UdpReceiveResult result)
    {
        try
        {
            var json = Encoding.UTF8.GetString(result.Buffer);
            var message = JsonSerializer.Deserialize<DiscoveryMessage>(json, JsonOptions);
            if (message == null ||
                !string.Equals(message.Type, DiscoverMessageType, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(message.RequestId))
            {
                return null;
            }

            return message;
        }
        catch
        {
            return null;
        }
    }

    private static EventRegisterAdvertisement? TryParseAdvertisement(UdpReceiveResult result, string requestId)
    {
        try
        {
            var json = Encoding.UTF8.GetString(result.Buffer);
            var message = JsonSerializer.Deserialize<AdvertisementMessage>(json, JsonOptions);
            if (message == null ||
                !string.Equals(message.Type, AdvertisementMessageType, StringComparison.Ordinal) ||
                !string.Equals(message.RequestId, requestId, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(message.RegisterId))
            {
                return null;
            }

            var endpoint = result.RemoteEndPoint.ToString();
            return new EventRegisterAdvertisement(
                message.RegisterId,
                NormalizeRequiredText(message.EventName, "Default Event"),
                NormalizeRequiredText(message.RegisterName, "Kasse"),
                NormalizeRequiredText(message.HostName, result.RemoteEndPoint.Address.ToString()),
                endpoint,
                DateTimeOffset.UtcNow);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildStableRegisterId(string eventName, string registerName)
    {
        var source = $"{Environment.MachineName}|{NormalizeRequiredText(eventName, "Default Event")}|{NormalizeRequiredText(registerName, "Kasse")}";
        var bytes = Encoding.UTF8.GetBytes(source);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..24];
    }

    private static string NormalizeRequiredText(string? text, string fallback)
    {
        var normalized = text?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private sealed record DiscoveryMessage(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("request_id")] string RequestId,
        [property: JsonPropertyName("event_name")] string EventName);

    private sealed record AdvertisementMessage(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("request_id")] string RequestId,
        [property: JsonPropertyName("register_id")] string RegisterId,
        [property: JsonPropertyName("event_name")] string EventName,
        [property: JsonPropertyName("register_name")] string RegisterName,
        [property: JsonPropertyName("host_name")] string HostName);
}
