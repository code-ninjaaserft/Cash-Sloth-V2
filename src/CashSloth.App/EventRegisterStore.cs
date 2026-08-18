using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CashSloth.App;

internal sealed record EventClientRegister(
    string Id,
    string EventName,
    string RegisterName,
    string HostName,
    string Endpoint,
    DateTimeOffset LastSeenUtc);

internal sealed class EventRegisterStore
{
    private const int CurrentSchemaVersion = 1;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    internal EventRegisterStore()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        FilePath = Path.Combine(localAppData, "CashSloth", "event.registers.json");
    }

    internal EventRegisterStore(string filePath)
    {
        FilePath = filePath;
    }

    internal string FilePath { get; }

    internal bool TryLoad(out List<EventClientRegister> registers, out string? error)
    {
        registers = new List<EventClientRegister>();
        error = null;

        if (!File.Exists(FilePath))
        {
            return true;
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            var document = JsonSerializer.Deserialize<EventRegisterStoreDocument>(json, _jsonOptions);
            if (document == null)
            {
                return true;
            }

            if (document.SchemaVersion > CurrentSchemaVersion)
            {
                error = $"Unsupported event register schema version {document.SchemaVersion}.";
                return false;
            }

            registers = document.Registers
                .Where(register => !string.IsNullOrWhiteSpace(register.Id))
                .Select(register => new EventClientRegister(
                    register.Id.Trim(),
                    NormalizeRequiredText(register.EventName, "Default Event"),
                    NormalizeRequiredText(register.RegisterName, "Kasse"),
                    register.HostName?.Trim() ?? string.Empty,
                    register.Endpoint?.Trim() ?? string.Empty,
                    register.LastSeenUtc))
                .OrderBy(register => register.EventName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(register => register.RegisterName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal bool TryUpsert(EventClientRegister register, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(register.Id))
        {
            error = "Register id is required.";
            return false;
        }

        if (!TryLoad(out var registers, out error))
        {
            return false;
        }

        registers.RemoveAll(existing => string.Equals(existing.Id, register.Id, StringComparison.OrdinalIgnoreCase));
        registers.Add(register with
        {
            Id = register.Id.Trim(),
            EventName = NormalizeRequiredText(register.EventName, "Default Event"),
            RegisterName = NormalizeRequiredText(register.RegisterName, "Kasse"),
            HostName = register.HostName.Trim(),
            Endpoint = register.Endpoint.Trim(),
            LastSeenUtc = register.LastSeenUtc == default ? DateTimeOffset.UtcNow : register.LastSeenUtc
        });

        return TrySave(registers, out error);
    }

    internal bool TryRemove(string registerId, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(registerId))
        {
            error = "Register id is required.";
            return false;
        }

        if (!TryLoad(out var registers, out error))
        {
            return false;
        }

        var removed = registers.RemoveAll(register => string.Equals(register.Id, registerId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            error = $"Register '{registerId}' does not exist.";
            return false;
        }

        return TrySave(registers, out error);
    }

    private bool TrySave(IReadOnlyCollection<EventClientRegister> registers, out string? error)
    {
        error = null;

        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var document = new EventRegisterStoreDocument(
                CurrentSchemaVersion,
                registers
                    .OrderBy(register => register.EventName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(register => register.RegisterName, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
            var json = JsonSerializer.Serialize(document, _jsonOptions);
            File.WriteAllText(FilePath, json);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string NormalizeRequiredText(string? text, string fallback)
    {
        var normalized = text?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }
}

internal sealed record EventRegisterStoreDocument(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("registers")] EventClientRegister[] Registers);
