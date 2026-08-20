using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CashSloth.Server.Infrastructure;
using CashSloth.Server.Security;
using Microsoft.Data.Sqlite;

namespace CashSloth.Server.Services;

public sealed record BackupResult(string Path, DateTimeOffset CreatedAtUtc, long SizeBytes);

public sealed class BackupService(
    ServerPaths paths,
    ServerSettings settings,
    ServerSettingsStore settingsStore)
{
    private static readonly byte[] PortableMagic = "CSBK1"u8.ToArray();
    private const int Pbkdf2Iterations = 600_000;

    public async Task<BackupResult?> CreateLocalBackupAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        paths.EnsureDirectories();
        if (!File.Exists(paths.DatabasePath))
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var safeReason = new string(reason.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(safeReason))
        {
            safeReason = "manual";
        }
        var destination = Path.Combine(paths.BackupsPath, $"cashsloth-{now:yyyyMMdd-HHmmss}-{safeReason}.sqlite3");
        await SnapshotDatabaseAsync(destination, cancellationToken);
        RetainLatestLocalBackups(10);
        return new BackupResult(destination, now, new FileInfo(destination).Length);
    }

    public async Task<BackupResult> CreatePortableBackupAsync(
        string destinationPath,
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        ValidatePassphrase(passphrase);
        if (!File.Exists(paths.DatabasePath))
        {
            throw new InvalidOperationException("Die Serverdatenbank existiert noch nicht.");
        }

        var temporarySnapshot = Path.Combine(Path.GetTempPath(), $"cashsloth-server-{Guid.NewGuid():N}.sqlite3");
        try
        {
            await SnapshotDatabaseAsync(temporarySnapshot, cancellationToken);
            using var zipStream = new MemoryStream();
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                AddFile(archive, temporarySnapshot, "database.sqlite3");

                var signingKey = SecretProtector.Unprotect(await File.ReadAllBytesAsync(paths.SigningKeyPath, cancellationToken));
                try
                {
                    AddBytes(archive, signingKey, "secrets/server-signing-key.pkcs8");
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(signingKey);
                }

                AddFile(archive, paths.SigningKeyMetadataPath, "secrets/server-signing-key.json");
                if (File.Exists(paths.TunnelTokenPath))
                {
                    var token = SecretProtector.ReadProtectedText(paths.TunnelTokenPath)!;
                    AddBytes(archive, Encoding.UTF8.GetBytes(token), "secrets/tunnel-token.txt");
                }

                var portableSettings = settings with { DataPath = string.Empty, CloudflaredPath = string.Empty };
                AddBytes(
                    archive,
                    JsonSerializer.SerializeToUtf8Bytes(portableSettings, JsonOptions),
                    "server.settings.json");

                if (Directory.Exists(paths.DataProtectionKeysPath))
                {
                    foreach (var file in Directory.EnumerateFiles(paths.DataProtectionKeysPath, "*", SearchOption.AllDirectories))
                    {
                        var relative = Path.GetRelativePath(paths.DataProtectionKeysPath, file).Replace('\\', '/');
                        AddFile(archive, file, $"data-protection-keys/{relative}");
                    }
                }
            }

            var encrypted = Encrypt(zipStream.ToArray(), passphrase);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
            await File.WriteAllBytesAsync(destinationPath, encrypted, cancellationToken);
            return new BackupResult(destinationPath, DateTimeOffset.UtcNow, encrypted.LongLength);
        }
        finally
        {
            if (File.Exists(temporarySnapshot))
            {
                File.Delete(temporarySnapshot);
            }
        }
    }

    public async Task RestorePortableBackupAsync(
        string sourcePath,
        string passphrase,
        bool serverIsStopped,
        CancellationToken cancellationToken = default)
    {
        if (!serverIsStopped)
        {
            throw new InvalidOperationException("Restore ist nur bei gestopptem Server erlaubt.");
        }
        ValidatePassphrase(passphrase);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Backup wurde nicht gefunden.", sourcePath);
        }

        await CreateLocalBackupAsync("pre-restore", cancellationToken);
        var plain = Decrypt(await File.ReadAllBytesAsync(sourcePath, cancellationToken), passphrase);
        try
        {
            using var stream = new MemoryStream(plain, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var database = RequireEntry(archive, "database.sqlite3");
            var signingKey = RequireEntry(archive, "secrets/server-signing-key.pkcs8");
            var signingMetadata = RequireEntry(archive, "secrets/server-signing-key.json");

            paths.EnsureDirectories();
            SqliteConnection.ClearAllPools();
            await ExtractAtomicallyAsync(database, paths.DatabasePath, cancellationToken);

            var privateKey = await ReadEntryAsync(signingKey, cancellationToken);
            try
            {
                await File.WriteAllBytesAsync(paths.SigningKeyPath, SecretProtector.Protect(privateKey), cancellationToken);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(privateKey);
            }
            await ExtractAtomicallyAsync(signingMetadata, paths.SigningKeyMetadataPath, cancellationToken);

            var tokenEntry = archive.GetEntry("secrets/tunnel-token.txt");
            if (tokenEntry is not null)
            {
                var tokenBytes = await ReadEntryAsync(tokenEntry, cancellationToken);
                try
                {
                    SecretProtector.WriteProtectedText(paths.TunnelTokenPath, Encoding.UTF8.GetString(tokenBytes));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(tokenBytes);
                }
            }

            foreach (var entry in archive.Entries.Where(value => value.FullName.StartsWith("data-protection-keys/", StringComparison.Ordinal)))
            {
                var relative = entry.FullName["data-protection-keys/".Length..];
                if (string.IsNullOrWhiteSpace(relative))
                {
                    continue;
                }
                var destination = Path.GetFullPath(Path.Combine(paths.DataProtectionKeysPath, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!destination.StartsWith(Path.GetFullPath(paths.DataProtectionKeysPath), StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Backup enthält einen ungültigen Dateipfad.");
                }
                await ExtractAtomicallyAsync(entry, destination, cancellationToken);
            }

            var settingsEntry = archive.GetEntry("server.settings.json");
            if (settingsEntry is not null)
            {
                var restoredBytes = await ReadEntryAsync(settingsEntry, cancellationToken);
                var restored = JsonSerializer.Deserialize<ServerSettings>(restoredBytes, JsonOptions);
                if (restored is not null)
                {
                    settingsStore.Save(restored with
                    {
                        DataPath = settings.DataPath,
                        CloudflaredPath = settings.CloudflaredPath
                    });
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public DateTimeOffset? GetLatestLocalBackupUtc()
    {
        if (!Directory.Exists(paths.BackupsPath))
        {
            return null;
        }
        return Directory.EnumerateFiles(paths.BackupsPath, "cashsloth-*.sqlite3")
            .Select(path => new FileInfo(path))
            .OrderByDescending(value => value.CreationTimeUtc)
            .Select(value => (DateTimeOffset?)value.CreationTimeUtc)
            .FirstOrDefault();
    }

    private async Task SnapshotDatabaseAsync(string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var source = new SqliteConnection($"Data Source={paths.DatabasePath};Mode=ReadOnly;Default Timeout=10;Pooling=False");
        await using var target = new SqliteConnection($"Data Source={destination};Mode=ReadWriteCreate;Pooling=False");
        await source.OpenAsync(cancellationToken);
        await target.OpenAsync(cancellationToken);
        source.BackupDatabase(target);
    }

    private void RetainLatestLocalBackups(int count)
    {
        foreach (var file in Directory.EnumerateFiles(paths.BackupsPath, "cashsloth-*.sqlite3")
                     .Select(value => new FileInfo(value))
                     .OrderByDescending(value => value.CreationTimeUtc)
                     .Skip(count))
        {
            file.Delete();
        }
    }

    private static byte[] Encrypt(byte[] plain, string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Encrypt(nonce, plain, cipher, tag, PortableMagic);
            using var output = new MemoryStream();
            output.Write(PortableMagic);
            output.Write(BitConverter.GetBytes(Pbkdf2Iterations));
            output.Write(salt);
            output.Write(nonce);
            output.Write(tag);
            output.Write(cipher);
            return output.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] Decrypt(byte[] encrypted, string passphrase)
    {
        const int headerLength = 5 + 4 + 16 + 12 + 16;
        if (encrypted.Length <= headerLength || !encrypted.AsSpan(0, PortableMagic.Length).SequenceEqual(PortableMagic))
        {
            throw new InvalidDataException("Datei ist kein CashSloth-Serverbackup.");
        }
        var offset = PortableMagic.Length;
        var iterations = BitConverter.ToInt32(encrypted, offset);
        offset += 4;
        if (iterations is < 100_000 or > 5_000_000)
        {
            throw new InvalidDataException("Backup verwendet ungültige Schlüsselparameter.");
        }
        var salt = encrypted.AsSpan(offset, 16).ToArray();
        offset += 16;
        var nonce = encrypted.AsSpan(offset, 12).ToArray();
        offset += 12;
        var tag = encrypted.AsSpan(offset, 16).ToArray();
        offset += 16;
        var cipher = encrypted.AsSpan(offset).ToArray();
        var plain = new byte[cipher.Length];
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, iterations, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, cipher, tag, plain, PortableMagic);
            return plain;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plain);
            throw new InvalidDataException("Passphrase ist falsch oder das Backup wurde verändert.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static void AddFile(ZipArchive archive, string sourcePath, string entryName)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Erforderliche Backupdatei fehlt.", sourcePath);
        }
        archive.CreateEntryFromFile(sourcePath, entryName, CompressionLevel.Optimal);
    }

    private static void AddBytes(ZipArchive archive, ReadOnlySpan<byte> bytes, string entryName)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static ZipArchiveEntry RequireEntry(ZipArchive archive, string name) =>
        archive.GetEntry(name) ?? throw new InvalidDataException($"Backupbestandteil '{name}' fehlt.");

    private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        return memory.ToArray();
    }

    private static async Task ExtractAtomicallyAsync(
        ZipArchiveEntry entry,
        string destination,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".restore.tmp";
        await using (var source = entry.Open())
        await using (var target = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await source.CopyToAsync(target, cancellationToken);
        }
        File.Move(temporary, destination, overwrite: true);
    }

    private static void ValidatePassphrase(string passphrase)
    {
        if (passphrase.Length < 12)
        {
            throw new ArgumentException("Die Backup-Passphrase muss mindestens 12 Zeichen lang sein.", nameof(passphrase));
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}
