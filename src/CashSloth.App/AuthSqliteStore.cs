using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace CashSloth.App;

internal enum UserRole
{
    Downloader = 0,
    Creator = 1,
    Admin = 2
}

internal sealed record AuthSessionUser(string Username, UserRole Role);

internal sealed record AuthAccountSummary(string Username, UserRole Role, bool IsEnabled)
{
    public override string ToString()
    {
        return IsEnabled ? $"{Username} ({Role})" : $"{Username} ({Role}) - disabled";
    }
}

internal sealed class AuthSqliteStore
{
    private const int CurrentSchemaVersion = 1;
    private const string DefaultAdminUsername = "admin";
    private const string DefaultAdminPassword = "admin";
    private const string LocalAdminRecoveryTicketFileName = "accounts.admin.local.ticket";
    private static readonly byte[] LocalAdminRecoveryEntropy = "CashSloth.AdminRecovery.v1"u8.ToArray();

    private readonly string _localAdminRecoveryTicketPath;

    internal AuthSqliteStore()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        FilePath = Path.Combine(localAppData, "CashSloth", "accounts.sqlite3");
        _localAdminRecoveryTicketPath = Path.Combine(Path.GetDirectoryName(FilePath) ?? string.Empty, LocalAdminRecoveryTicketFileName);
    }

    internal AuthSqliteStore(string filePath)
    {
        FilePath = filePath;
        _localAdminRecoveryTicketPath = Path.Combine(Path.GetDirectoryName(FilePath) ?? string.Empty, LocalAdminRecoveryTicketFileName);
    }

    internal string FilePath { get; }

    internal bool TryEnsureInitialized(out bool seededDefaultAdmin, out string? error)
    {
        seededDefaultAdmin = false;
        error = null;

        try
        {
            using var connection = OpenConnection();
            if (!TryEnsureSchema(connection, out error))
            {
                return false;
            }

            if (ReadAccountCount(connection) == 0)
            {
                var now = DateTimeOffset.UtcNow.ToString("O");
                InsertAccount(connection, DefaultAdminUsername, HashPassword(DefaultAdminPassword), UserRole.Admin, true, now, now);
                seededDefaultAdmin = true;
            }

            if (!TryEnsureLocalAdminRecoveryTicket(out error))
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal bool TryAuthenticateLocalAdminBypass(out AuthSessionUser? user, out string? error)
    {
        user = null;
        error = null;

        if (!TryEnsureInitialized(out _, out error))
        {
            return false;
        }

        if (!TryValidateLocalAdminRecoveryTicket(out error))
        {
            return false;
        }

        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT username
                FROM accounts
                WHERE role = $role
                  AND is_enabled = 1
                ORDER BY username COLLATE NOCASE
                LIMIT 1;";
            command.Parameters.AddWithValue("$role", UserRole.Admin.ToString());

            var username = command.ExecuteScalar() as string;
            if (string.IsNullOrWhiteSpace(username))
            {
                error = "No enabled admin account exists.";
                return false;
            }

            user = new AuthSessionUser(username, UserRole.Admin);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal bool TryAuthenticate(string username, string password, out AuthSessionUser? user, out string? error)
    {
        user = null;
        error = null;

        var normalizedUsername = NormalizeUsername(username);
        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            error = "Username is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            error = "Password is required.";
            return false;
        }

        if (!TryEnsureInitialized(out _, out error))
        {
            return false;
        }

        try
        {
            using var connection = OpenConnection();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT username, password_hash, role, is_enabled
                FROM accounts
                WHERE username = $username
                LIMIT 1;";
            command.Parameters.AddWithValue("$username", normalizedUsername);

            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                error = "Invalid credentials.";
                return false;
            }

            var resolvedUsername = reader.GetString(0);
            var passwordHash = reader.GetString(1);
            var roleText = reader.GetString(2);
            var isEnabled = reader.GetInt64(3) != 0;

            if (!isEnabled)
            {
                error = "Account is disabled.";
                return false;
            }

            if (!VerifyPassword(password, passwordHash))
            {
                error = "Invalid credentials.";
                return false;
            }

            if (!Enum.TryParse<UserRole>(roleText, true, out var role))
            {
                error = $"Invalid account role '{roleText}'.";
                return false;
            }

            user = new AuthSessionUser(resolvedUsername, role);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal bool TryListAccounts(out List<AuthAccountSummary> summaries, out string? error)
    {
        summaries = new List<AuthAccountSummary>();
        error = null;

        if (!TryEnsureInitialized(out _, out error))
        {
            return false;
        }

        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT username, role, is_enabled
                FROM accounts
                ORDER BY username COLLATE NOCASE;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var username = reader.GetString(0);
                var roleText = reader.GetString(1);
                var isEnabled = reader.GetInt64(2) != 0;

                if (!Enum.TryParse<UserRole>(roleText, true, out var role))
                {
                    role = UserRole.Downloader;
                }

                summaries.Add(new AuthAccountSummary(username, role, isEnabled));
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal bool TryUpsertAccount(string username, string? password, UserRole role, bool isEnabled, out string? error)
    {
        error = null;

        var normalizedUsername = NormalizeUsername(username);
        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            error = "Username is required.";
            return false;
        }

        if (!TryEnsureInitialized(out _, out error))
        {
            return false;
        }

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            var existing = ReadAccount(connection, transaction, normalizedUsername);
            if (existing == null)
            {
                if (string.IsNullOrWhiteSpace(password))
                {
                    error = "Password is required for new accounts.";
                    transaction.Rollback();
                    return false;
                }

                var now = DateTimeOffset.UtcNow.ToString("O");
                InsertAccount(connection, normalizedUsername, HashPassword(password), role, isEnabled, now, now, transaction);
                transaction.Commit();
                return true;
            }

            if (existing.IsEnabled && existing.Role == UserRole.Admin && (!isEnabled || role != UserRole.Admin))
            {
                if (CountEnabledAdminsExcluding(connection, transaction, normalizedUsername) == 0)
                {
                    error = "At least one enabled admin account must remain.";
                    transaction.Rollback();
                    return false;
                }
            }

            var passwordHash = string.IsNullOrWhiteSpace(password) ? existing.PasswordHash : HashPassword(password);
            var updatedUtc = DateTimeOffset.UtcNow.ToString("O");

            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = @"
                UPDATE accounts
                SET password_hash = $password_hash,
                    role = $role,
                    is_enabled = $is_enabled,
                    updated_utc = $updated_utc
                WHERE username = $username;";
            update.Parameters.AddWithValue("$password_hash", passwordHash);
            update.Parameters.AddWithValue("$role", role.ToString());
            update.Parameters.AddWithValue("$is_enabled", isEnabled ? 1 : 0);
            update.Parameters.AddWithValue("$updated_utc", updatedUtc);
            update.Parameters.AddWithValue("$username", normalizedUsername);
            update.ExecuteNonQuery();

            transaction.Commit();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal bool TryDeleteAccount(string username, out string? error)
    {
        error = null;

        var normalizedUsername = NormalizeUsername(username);
        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            error = "Username is required.";
            return false;
        }

        if (!TryEnsureInitialized(out _, out error))
        {
            return false;
        }

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            var existing = ReadAccount(connection, transaction, normalizedUsername);
            if (existing == null)
            {
                error = $"Account '{normalizedUsername}' does not exist.";
                transaction.Rollback();
                return false;
            }

            if (existing.IsEnabled && existing.Role == UserRole.Admin && CountEnabledAdminsExcluding(connection, transaction, normalizedUsername) == 0)
            {
                error = "At least one enabled admin account must remain.";
                transaction.Rollback();
                return false;
            }

            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM accounts WHERE username = $username;";
            delete.Parameters.AddWithValue("$username", normalizedUsername);
            delete.ExecuteNonQuery();

            transaction.Commit();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private SqliteConnection OpenConnection()
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connection = new SqliteConnection($"Data Source={FilePath}");
        connection.Open();
        return connection;
    }

    private static bool TryEnsureSchema(SqliteConnection connection, out string? error)
    {
        error = null;

        var userVersion = ReadUserVersion(connection);
        if (userVersion > CurrentSchemaVersion)
        {
            error = $"Unsupported accounts schema version {userVersion}.";
            return false;
        }

        if (userVersion == 0)
        {
            using var create = connection.CreateCommand();
            create.CommandText = @"
                CREATE TABLE IF NOT EXISTS accounts (
                    username TEXT PRIMARY KEY COLLATE NOCASE,
                    password_hash TEXT NOT NULL,
                    role TEXT NOT NULL,
                    is_enabled INTEGER NOT NULL,
                    created_utc TEXT NOT NULL,
                    updated_utc TEXT NOT NULL
                );";
            create.ExecuteNonQuery();

            using var version = connection.CreateCommand();
            version.CommandText = $"PRAGMA user_version = {CurrentSchemaVersion};";
            version.ExecuteNonQuery();
        }

        return true;
    }

    private static int ReadUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var value = command.ExecuteScalar();
        return value is long version ? (int)version : 0;
    }

    private static long ReadAccountCount(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM accounts;";
        return command.ExecuteScalar() is long count ? count : 0;
    }

    private static AuthAccountRow? ReadAccount(SqliteConnection connection, SqliteTransaction transaction, string username)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            SELECT username, password_hash, role, is_enabled
            FROM accounts
            WHERE username = $username
            LIMIT 1;";
        command.Parameters.AddWithValue("$username", username);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var storedRole = Enum.TryParse<UserRole>(reader.GetString(2), true, out var role)
            ? role
            : UserRole.Downloader;
        return new AuthAccountRow(
            reader.GetString(0),
            reader.GetString(1),
            storedRole,
            reader.GetInt64(3) != 0);
    }

    private static long CountEnabledAdminsExcluding(SqliteConnection connection, SqliteTransaction transaction, string username)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            SELECT COUNT(*)
            FROM accounts
            WHERE role = $role
              AND is_enabled = 1
              AND username <> $username;";
        command.Parameters.AddWithValue("$role", UserRole.Admin.ToString());
        command.Parameters.AddWithValue("$username", username);
        return command.ExecuteScalar() is long count ? count : 0;
    }

    private static void InsertAccount(
        SqliteConnection connection,
        string username,
        string passwordHash,
        UserRole role,
        bool isEnabled,
        string createdUtc,
        string updatedUtc,
        SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO accounts (username, password_hash, role, is_enabled, created_utc, updated_utc)
            VALUES ($username, $password_hash, $role, $is_enabled, $created_utc, $updated_utc);";
        command.Parameters.AddWithValue("$username", username);
        command.Parameters.AddWithValue("$password_hash", passwordHash);
        command.Parameters.AddWithValue("$role", role.ToString());
        command.Parameters.AddWithValue("$is_enabled", isEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$created_utc", createdUtc);
        command.Parameters.AddWithValue("$updated_utc", updatedUtc);
        command.ExecuteNonQuery();
    }

    private static string NormalizeUsername(string? username)
    {
        return username?.Trim() ?? string.Empty;
    }

    private bool TryEnsureLocalAdminRecoveryTicket(out string? error)
    {
        error = null;

        try
        {
            var directory = Path.GetDirectoryName(_localAdminRecoveryTicketPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(_localAdminRecoveryTicketPath) &&
                TryValidateLocalAdminRecoveryTicket(out _))
            {
                return true;
            }

            var fingerprint = GetMachineFingerprint();
            var payload = $"{fingerprint}|{Guid.NewGuid():N}|{DateTimeOffset.UtcNow:O}";
            var payloadBytes = Encoding.UTF8.GetBytes(payload);
            var protectedBytes = ProtectedData.Protect(payloadBytes, LocalAdminRecoveryEntropy, DataProtectionScope.LocalMachine);
            File.WriteAllBytes(_localAdminRecoveryTicketPath, protectedBytes);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private bool TryValidateLocalAdminRecoveryTicket(out string? error)
    {
        error = null;

        try
        {
            if (!File.Exists(_localAdminRecoveryTicketPath))
            {
                error = "Local admin recovery ticket does not exist.";
                return false;
            }

            var protectedBytes = File.ReadAllBytes(_localAdminRecoveryTicketPath);
            var payloadBytes = ProtectedData.Unprotect(protectedBytes, LocalAdminRecoveryEntropy, DataProtectionScope.LocalMachine);
            var payload = Encoding.UTF8.GetString(payloadBytes);

            var separatorIndex = payload.IndexOf('|');
            if (separatorIndex <= 0)
            {
                error = "Local admin recovery ticket format is invalid.";
                return false;
            }

            var ticketFingerprint = payload[..separatorIndex];
            var currentFingerprint = GetMachineFingerprint();
            if (!string.Equals(ticketFingerprint, currentFingerprint, StringComparison.Ordinal))
            {
                error = "Local admin recovery ticket is not valid for this laptop.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string GetMachineFingerprint()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            var machineGuid = key?.GetValue("MachineGuid") as string;
            if (!string.IsNullOrWhiteSpace(machineGuid))
            {
                return machineGuid.Trim();
            }
        }
        catch
        {
            // Fall back to a weaker fingerprint if registry access fails.
        }

        return $"{Environment.MachineName}|{Environment.OSVersion.VersionString}";
    }

    private static string HashPassword(string password)
    {
        const int iterations = 100_000;
        Span<byte> salt = stackalloc byte[16];
        RandomNumberGenerator.Fill(salt);

        Span<byte> hash = stackalloc byte[32];
        Rfc2898DeriveBytes.Pbkdf2(password, salt, hash, iterations, HashAlgorithmName.SHA256);

        return $"PBKDF2${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string encodedHash)
    {
        if (string.IsNullOrWhiteSpace(encodedHash))
        {
            return false;
        }

        var parts = encodedHash.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || !string.Equals(parts[0], "PBKDF2", StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[2]);
        var expectedHash = Convert.FromBase64String(parts[3]);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private sealed record AuthAccountRow(string Username, string PasswordHash, UserRole Role, bool IsEnabled);
}
