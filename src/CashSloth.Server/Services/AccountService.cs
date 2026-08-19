using System.Security.Cryptography;
using CashSloth.Contracts;
using CashSloth.Server.Api;
using CashSloth.Server.Data;
using CashSloth.Server.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CashSloth.Server.Services;

public sealed class AccountService(
    ServerDbContext db,
    UserManager<ServerUser> userManager,
    DeviceProofService deviceProof,
    TokenService tokenService,
    AuditService audit)
{
    public async Task<RegistrationResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var payloadHash = DeviceProofService.BuildPayloadHash(request.Username, request.Password);
        var device = await deviceProof.VerifyAndConsumeAsync(request.Proof, "register", payloadHash, cancellationToken);
        var username = NormalizeUsername(request.Username);

        var user = new ServerUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = username,
            IsApproved = false,
            IsActive = true,
            MustChangePassword = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            LockoutEnabled = true
        };
        var createResult = await userManager.CreateAsync(user, request.Password);
        ThrowIfIdentityFailed(createResult, "registration_failed", "Account konnte nicht registriert werden.");
        var roleResult = await userManager.AddToRoleAsync(user, CashSlothRoles.User);
        ThrowIfIdentityFailed(roleResult, "role_assignment_failed", "Standardrolle konnte nicht gesetzt werden.");

        await audit.WriteAsync(
            $"device:{device.Id:N}",
            "account.register",
            "account",
            user.Id,
            user.UserName,
            cancellationToken: cancellationToken);
        return new RegistrationResponse(user.Id, user.UserName!, false);
    }

    public async Task<AuthTokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var payloadHash = DeviceProofService.BuildPayloadHash(request.Username, request.Password);
        var device = await deviceProof.VerifyAndConsumeAsync(request.Proof, "login", payloadHash, cancellationToken);
        var user = await userManager.FindByNameAsync(request.Username.Trim());
        if (user is null)
        {
            throw new ApiProblemException(401, "invalid_credentials", "Benutzername oder Passwort ist falsch.");
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            throw new ApiProblemException(429, "account_temporarily_locked", "Account ist vorübergehend gesperrt.");
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            throw new ApiProblemException(401, "invalid_credentials", "Benutzername oder Passwort ist falsch.");
        }

        await userManager.ResetAccessFailedCountAsync(user);
        EnsureCanSignIn(user);
        var role = await GetRoleAsync(user);
        var tokens = await CreateSessionTokensAsync(user, role, device, cancellationToken);
        await audit.WriteAsync(user.UserName!, "auth.login", "session", tokens.User.SessionId.ToString("N"), device.Name, cancellationToken: cancellationToken);
        return tokens;
    }

    public async Task<AuthTokenResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        var payloadHash = DeviceProofService.BuildPayloadHash(request.RefreshToken);
        var device = await deviceProof.VerifyAndConsumeAsync(request.Proof, "refresh", payloadHash, cancellationToken);
        var tokenHash = CryptographicHelpers.Sha256Base64Url(request.RefreshToken);
        var session = await db.LoginSessions
            .Include(value => value.User)
            .SingleOrDefaultAsync(value => value.RefreshTokenHash == tokenHash, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        if (session is null || session.RevokedAtUtc is not null || session.ExpiresAtUtc <= now || session.DeviceId != device.Id)
        {
            throw new ApiProblemException(401, "invalid_refresh_token", "Refresh-Token ist ungültig oder abgelaufen.");
        }

        EnsureCanSignIn(session.User);
        var role = await GetRoleAsync(session.User);
        var refreshToken = CryptographicHelpers.RandomToken(48);
        session.RefreshTokenHash = CryptographicHelpers.Sha256Base64Url(refreshToken);
        session.LastRefreshedAtUtc = now;
        session.ExpiresAtUtc = now.Add(TokenService.RefreshTokenLifetime);
        await db.SaveChangesAsync(cancellationToken);

        var access = tokenService.CreateAccessToken(session.User, role, device.Id, session.Id);
        return new AuthTokenResponse(
            access.Token,
            access.ExpiresAtUtc,
            refreshToken,
            session.ExpiresAtUtc,
            ToProfile(session.User, role, device.Id, session.Id));
    }

    public async Task LogoutAsync(string sessionId, string? refreshToken, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(sessionId, out var parsedSessionId))
        {
            return;
        }

        var session = await db.LoginSessions.SingleOrDefaultAsync(value => value.Id == parsedSessionId, cancellationToken);
        if (session is null || session.RevokedAtUtc is not null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(refreshToken) &&
            !CryptographicHelpers.FixedTimeEquals(
                session.RefreshTokenHash,
                CryptographicHelpers.Sha256Base64Url(refreshToken)))
        {
            throw new ApiProblemException(401, "invalid_refresh_token", "Refresh-Token gehört nicht zu dieser Sitzung.");
        }

        session.RevokedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<UserProfileResponse> GetProfileAsync(
        string userId,
        Guid deviceId,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new ApiProblemException(401, "invalid_session", "Benutzersitzung ist ungültig.");
        return ToProfile(user, await GetRoleAsync(user), deviceId, sessionId);
    }

    public async Task ChangePasswordAsync(
        string userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new ApiProblemException(401, "invalid_session", "Benutzersitzung ist ungültig.");
        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        ThrowIfIdentityFailed(result, "password_change_failed", "Passwort konnte nicht geändert werden.");
        user.MustChangePassword = false;
        await userManager.UpdateAsync(user);
        await audit.WriteAsync(user.UserName!, "account.password-change", "account", user.Id, cancellationToken: cancellationToken);
    }

    public async Task CreateFirstAdminAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        username = NormalizeUsername(username);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await db.Users.AnyAsync(cancellationToken))
        {
            throw new InvalidOperationException("Die Ersteinrichtung ist bereits abgeschlossen.");
        }

        var user = new ServerUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = username,
            IsApproved = true,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ApprovedAtUtc = DateTimeOffset.UtcNow,
            LockoutEnabled = true
        };
        var createResult = await userManager.CreateAsync(user, password);
        ThrowIfIdentityFailed(createResult, "setup_failed", "Erster Administrator konnte nicht erstellt werden.");
        var roleResult = await userManager.AddToRoleAsync(user, CashSlothRoles.Admin);
        ThrowIfIdentityFailed(roleResult, "setup_failed", "Administratorrolle konnte nicht gesetzt werden.");
        await audit.WriteAsync("local-console", "account.first-admin", "account", user.Id, user.UserName, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminAccountResponse>> ListAccountsAsync(CancellationToken cancellationToken = default)
    {
        var users = await db.Users.AsNoTracking().OrderBy(value => value.UserName).ToListAsync(cancellationToken);
        var result = new List<AdminAccountResponse>(users.Count);
        foreach (var user in users)
        {
            result.Add(ToAdminAccount(user, await GetRoleAsync(user)));
        }
        return result;
    }

    public async Task SetApprovalAsync(string userId, bool approved, string actor, CancellationToken cancellationToken = default)
    {
        var user = await GetUserAsync(userId);
        user.IsApproved = approved;
        user.ApprovedAtUtc = approved ? DateTimeOffset.UtcNow : null;
        await userManager.UpdateAsync(user);
        await audit.WriteAsync(actor, approved ? "account.approve" : "account.unapprove", "account", user.Id, user.UserName, cancellationToken: cancellationToken);
    }

    public async Task SetRoleAsync(string userId, string role, string actor, CancellationToken cancellationToken = default)
    {
        if (!CashSlothRoles.All.Contains(role, StringComparer.Ordinal))
        {
            throw new ApiProblemException(400, "invalid_role", "Unbekannte Rolle.");
        }

        var user = await GetUserAsync(userId);
        var currentRole = await GetRoleAsync(user);
        if (currentRole == CashSlothRoles.Admin && role != CashSlothRoles.Admin)
        {
            await EnsureAnotherActiveAdminAsync(user.Id, cancellationToken);
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        if (currentRoles.Count > 0)
        {
            ThrowIfIdentityFailed(await userManager.RemoveFromRolesAsync(user, currentRoles), "role_change_failed", "Bisherige Rolle konnte nicht entfernt werden.");
        }
        ThrowIfIdentityFailed(await userManager.AddToRoleAsync(user, role), "role_change_failed", "Neue Rolle konnte nicht gesetzt werden.");
        await audit.WriteAsync(actor, "account.role-change", "account", user.Id, $"{currentRole} -> {role}", cancellationToken: cancellationToken);
    }

    public async Task SetActiveAsync(string userId, bool active, string actor, CancellationToken cancellationToken = default)
    {
        var user = await GetUserAsync(userId);
        if (!active && await userManager.IsInRoleAsync(user, CashSlothRoles.Admin))
        {
            await EnsureAnotherActiveAdminAsync(user.Id, cancellationToken);
        }

        user.IsActive = active;
        await userManager.UpdateAsync(user);
        if (!active)
        {
            var sessions = await db.LoginSessions
                .Where(value => value.UserId == user.Id && value.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var session in sessions)
            {
                session.RevokedAtUtc = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        await audit.WriteAsync(actor, active ? "account.enable" : "account.block", "account", user.Id, user.UserName, cancellationToken: cancellationToken);
    }

    public async Task<string> ResetPasswordAsync(string userId, string actor, CancellationToken cancellationToken = default)
    {
        var user = await GetUserAsync(userId);
        var temporaryPassword = CreateTemporaryPassword();
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, temporaryPassword);
        ThrowIfIdentityFailed(result, "password_reset_failed", "Passwort konnte nicht zurückgesetzt werden.");
        user.MustChangePassword = true;
        await userManager.UpdateAsync(user);
        await audit.WriteAsync(actor, "account.password-reset", "account", user.Id, user.UserName, cancellationToken: cancellationToken);
        return temporaryPassword;
    }

    private async Task<AuthTokenResponse> CreateSessionTokensAsync(
        ServerUser user,
        string role,
        Device device,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var refreshToken = CryptographicHelpers.RandomToken(48);
        var session = new LoginSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DeviceId = device.Id,
            RefreshTokenHash = CryptographicHelpers.Sha256Base64Url(refreshToken),
            CreatedAtUtc = now,
            LastRefreshedAtUtc = now,
            ExpiresAtUtc = now.Add(TokenService.RefreshTokenLifetime)
        };
        db.LoginSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        var access = tokenService.CreateAccessToken(user, role, device.Id, session.Id);
        return new AuthTokenResponse(
            access.Token,
            access.ExpiresAtUtc,
            refreshToken,
            session.ExpiresAtUtc,
            ToProfile(user, role, device.Id, session.Id));
    }

    private static void EnsureCanSignIn(ServerUser user)
    {
        if (!user.IsActive)
        {
            throw new ApiProblemException(403, "account_blocked", "Account ist gesperrt.");
        }
        if (!user.IsApproved)
        {
            throw new ApiProblemException(403, "account_pending", "Account wartet auf die Freigabe eines Administrators.");
        }
    }

    private async Task<string> GetRoleAsync(ServerUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return roles.FirstOrDefault() ?? CashSlothRoles.User;
    }

    private async Task<ServerUser> GetUserAsync(string userId) =>
        await userManager.FindByIdAsync(userId)
        ?? throw new ApiProblemException(404, "account_not_found", "Account wurde nicht gefunden.");

    private async Task EnsureAnotherActiveAdminAsync(string excludedUserId, CancellationToken cancellationToken)
    {
        var adminRoleId = await db.Roles
            .Where(value => value.NormalizedName == CashSlothRoles.Admin.ToUpperInvariant())
            .Select(value => value.Id)
            .SingleAsync(cancellationToken);
        var count = await (
            from user in db.Users
            join userRole in db.UserRoles on user.Id equals userRole.UserId
            where userRole.RoleId == adminRoleId && user.Id != excludedUserId && user.IsActive && user.IsApproved
            select user.Id).CountAsync(cancellationToken);
        if (count == 0)
        {
            throw new ApiProblemException(409, "last_active_admin", "Der letzte aktive Administrator darf nicht gesperrt oder herabgestuft werden.");
        }
    }

    private static UserProfileResponse ToProfile(ServerUser user, string role, Guid deviceId, Guid sessionId) =>
        new(user.Id, user.UserName ?? string.Empty, role, user.IsApproved, user.IsActive, user.MustChangePassword, deviceId, sessionId);

    private static AdminAccountResponse ToAdminAccount(ServerUser user, string role) =>
        new(user.Id, user.UserName ?? string.Empty, role, user.IsApproved, user.IsActive, user.MustChangePassword,
            user.AccessFailedCount, user.LockoutEnd, user.CreatedAtUtc);

    private static void ThrowIfIdentityFailed(IdentityResult result, string code, string message)
    {
        if (result.Succeeded)
        {
            return;
        }
        var fields = result.Errors
            .GroupBy(value => value.Code)
            .ToDictionary(group => group.Key, group => group.Select(DescribeIdentityError).ToArray());
        throw new ApiProblemException(400, code, message, fields);
    }

    private static string NormalizeUsername(string username)
    {
        var normalized = username.Trim();
        if (normalized.Length is < 3 or > 50)
        {
            throw new ApiProblemException(400, "invalid_username", "Benutzername muss 3 bis 50 Zeichen lang sein.");
        }
        return normalized;
    }

    private static string DescribeIdentityError(IdentityError error) => error.Code switch
    {
        "PasswordTooShort" => "Das Passwort muss mindestens 12 Zeichen lang sein.",
        "PasswordRequiresNonAlphanumeric" => "Das Passwort muss mindestens ein Sonderzeichen enthalten.",
        "PasswordRequiresDigit" => "Das Passwort muss mindestens eine Zahl enthalten.",
        "PasswordRequiresLower" => "Das Passwort muss mindestens einen Kleinbuchstaben enthalten.",
        "PasswordRequiresUpper" => "Das Passwort muss mindestens einen Grossbuchstaben enthalten.",
        "InvalidUserName" => "Der Benutzername darf nur Buchstaben, Zahlen, Punkt, Bindestrich und Unterstrich enthalten.",
        "DuplicateUserName" => "Dieser Benutzername ist bereits vergeben.",
        _ => error.Description
    };

    private static string CreateTemporaryPassword()
    {
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string digits = "23456789";
        const string symbols = "!@#$%_-";
        var all = lower + upper + digits + symbols;
        var chars = new char[16];
        chars[0] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        chars[1] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        chars[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        chars[3] = symbols[RandomNumberGenerator.GetInt32(symbols.Length)];
        for (var index = 4; index < chars.Length; index++)
        {
            chars[index] = all[RandomNumberGenerator.GetInt32(all.Length)];
        }
        for (var index = chars.Length - 1; index > 0; index--)
        {
            var swapIndex = RandomNumberGenerator.GetInt32(index + 1);
            (chars[index], chars[swapIndex]) = (chars[swapIndex], chars[index]);
        }
        return new string(chars);
    }
}
