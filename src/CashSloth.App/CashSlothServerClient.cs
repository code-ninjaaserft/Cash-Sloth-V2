using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CashSloth.Contracts;
using Microsoft.IdentityModel.Tokens;

namespace CashSloth.App;

internal sealed class CashSlothServerException(int statusCode, string code, string message) : Exception(message)
{
    internal int StatusCode { get; } = statusCode;
    internal string Code { get; } = code;
}

internal sealed class CashSlothServerClient : IDisposable
{
    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CashSlothServerStorage _storage;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    internal CashSlothServerClient(CashSlothServerStorage storage, HttpMessageHandler? handler = null)
    {
        _storage = storage;
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        Session = RestoreValidOfflineSession();
    }

    internal CashSlothClientConnection? Connection => _storage.LoadConnection();
    internal CashSlothClientSession? Session { get; private set; }
    internal bool IsPaired => Connection?.DeviceId is not null;
    internal string? AccessToken => Session?.AccessToken;
    internal string EventHubUrl => new Uri(new Uri(RequireConnection(requireDevice: true).Trust.HttpsUrl.TrimEnd('/') + "/"), "api/v1/events/hub").ToString();
    internal event Action<UserProfileResponse?>? SessionChanged;

    internal ServerTrustDocument ValidateTrustFile(string path)
    {
        var trust = JsonSerializer.Deserialize<ServerTrustDocument>(File.ReadAllText(path), _jsonOptions)
            ?? throw new InvalidDataException("Trust-Datei ist ungültig.");
        if (trust.Version != 1 ||
            string.IsNullOrWhiteSpace(trust.ServerId) ||
            string.IsNullOrWhiteSpace(trust.KeyId) ||
            string.IsNullOrWhiteSpace(trust.PublicKey) ||
            string.IsNullOrWhiteSpace(trust.Fingerprint) ||
            !Uri.TryCreate(trust.HttpsUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("Trust-Datei enthält keine gültige HTTPS-Serveradresse.");
        }
        byte[] publicKey;
        try
        {
            publicKey = Convert.FromBase64String(trust.PublicKey);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out var read);
            if (read != publicKey.Length || ecdsa.KeySize != 256)
            {
                throw new CryptographicException();
            }
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or ArgumentException)
        {
            throw new InvalidDataException("Trust-Datei enthält keinen gültigen ECDSA-P-256-Schlüssel.");
        }
        var fingerprint = Convert.ToHexString(SHA256.HashData(publicKey)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(fingerprint),
                Encoding.ASCII.GetBytes(trust.Fingerprint.ToLowerInvariant())))
        {
            throw new InvalidDataException("Fingerprint der Trust-Datei stimmt nicht mit dem Serverschlüssel überein.");
        }
        return trust;
    }

    internal void AcceptTrust(ServerTrustDocument trust)
    {
        var current = Connection;
        var sameServer = current is not null &&
                         current.Trust.ServerId == trust.ServerId &&
                         current.Trust.KeyId == trust.KeyId &&
                         string.Equals(current.Trust.Fingerprint, trust.Fingerprint, StringComparison.OrdinalIgnoreCase);
        _storage.SaveConnection(new CashSlothClientConnection(
            trust,
            sameServer ? current!.DeviceId : null,
            sameServer ? current!.DeviceName : null));
        if (!sameServer)
        {
            Session = null;
            _storage.ClearSession();
        }
    }

    internal async Task VerifyServerInfoAsync(CancellationToken cancellationToken = default)
    {
        var connection = RequireConnection(requireDevice: false);
        var info = await GetPublicAsync<ServerInfoResponse>("api/v1/server/info", cancellationToken);
        if (info.ServerId != connection.Trust.ServerId || info.KeyId != connection.Trust.KeyId)
        {
            throw new CashSlothServerException(409, "server_trust_mismatch", "Der erreichbare Server passt nicht zur importierten Trust-Datei.");
        }
    }

    internal async Task<DevicePairResponse> PairAsync(
        string pairingCode,
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        var connection = RequireConnection(requireDevice: false);
        await VerifyServerInfoAsync(cancellationToken);
        var code = new string(pairingCode.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        var name = deviceName.Trim();
        using var key = _storage.LoadOrCreateDeviceKey();
        var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        var proofText = $"cashsloth-pair-v1\n{code}\n{name}\n{publicKey}";
        var signature = Base64UrlEncode(key.SignData(Encoding.UTF8.GetBytes(proofText), HashAlgorithmName.SHA256));
        var response = await SendPublicAsync<DevicePairRequest, DevicePairResponse>(
            HttpMethod.Post,
            "api/v1/devices/pair",
            new DevicePairRequest(code, name, publicKey, signature),
            cancellationToken);
        _storage.SaveConnection(connection with { DeviceId = response.DeviceId, DeviceName = response.DeviceName });
        return response;
    }

    internal async Task<RegistrationResponse> RegisterAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var proof = await CreateProofAsync("register", BuildPayloadHash(username, password), cancellationToken);
        return await SendPublicAsync<RegisterRequest, RegistrationResponse>(
            HttpMethod.Post,
            "api/v1/auth/register",
            new RegisterRequest(username, password, proof),
            cancellationToken);
    }

    internal async Task<CashSlothClientSession> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var proof = await CreateProofAsync("login", BuildPayloadHash(username, password), cancellationToken);
        var response = await SendPublicAsync<LoginRequest, AuthTokenResponse>(
            HttpMethod.Post,
            "api/v1/auth/login",
            new LoginRequest(username, password, proof),
            cancellationToken);
        return SaveAndValidateSession(response);
    }

    internal async Task<CashSlothClientSession> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var requestedSession = Session ?? _storage.LoadSession()
            ?? throw new CashSlothServerException(401, "authentication_required", "Es ist keine Sitzung gespeichert.");
        var requestedRefreshToken = requestedSession.RefreshToken;

        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            var session = Session ?? _storage.LoadSession()
                ?? throw new CashSlothServerException(401, "authentication_required", "Es ist keine Sitzung gespeichert.");

            // Another request may already have rotated the one-time refresh token while this
            // request was waiting. Reuse that freshly validated session instead of submitting
            // the now-invalid previous token and clearing the successful login.
            if (!string.Equals(session.RefreshToken, requestedRefreshToken, StringComparison.Ordinal) &&
                IsAccessTokenLocallyValid(session.AccessToken, out _))
            {
                return session;
            }

            if (session.RefreshTokenExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                ClearSessionIfCurrent(session.RefreshToken);
                throw new CashSlothServerException(401, "refresh_token_expired", "Die gespeicherte Sitzung ist abgelaufen.");
            }
            try
            {
                var proof = await CreateProofAsync("refresh", BuildPayloadHash(session.RefreshToken), cancellationToken);
                var response = await SendPublicAsync<RefreshRequest, AuthTokenResponse>(
                    HttpMethod.Post,
                    "api/v1/auth/refresh",
                    new RefreshRequest(session.RefreshToken, proof),
                    cancellationToken);
                return SaveAndValidateSession(response);
            }
            catch (CashSlothServerException exception) when (exception.StatusCode is 401 or 403)
            {
                ClearSessionIfCurrent(session.RefreshToken);
                throw;
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    internal async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var session = Session;
        try
        {
            if (session is not null && IsAccessTokenLocallyValid(session.AccessToken, out _))
            {
                await SendAuthorizedAsync<LogoutRequest, object?>(
                    HttpMethod.Post,
                    "api/v1/auth/logout",
                    new LogoutRequest(session.RefreshToken),
                    cancellationToken,
                    allowNoContent: true);
            }
        }
        finally
        {
            ClearSession();
        }
    }

    internal async Task ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        await SendNoContentAsync(
            HttpMethod.Post,
            "api/v1/auth/change-password",
            new ChangePasswordRequest(currentPassword, newPassword),
            cancellationToken);

        if (Session is not null)
        {
            Session = Session with { User = Session.User with { MustChangePassword = false } };
            _storage.SaveSession(Session);
            SessionChanged?.Invoke(Session.User);
        }
    }

    internal async Task<UserProfileResponse> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        var profile = await SendAuthorizedAsync<object?, UserProfileResponse>(
            HttpMethod.Get,
            "api/v1/auth/me",
            null,
            cancellationToken)
            ?? throw new InvalidDataException("Server lieferte kein Benutzerprofil.");

        if (Session is not null)
        {
            Session = Session with { User = profile };
            _storage.SaveSession(Session);
            SessionChanged?.Invoke(profile);
        }
        return profile;
    }

    internal async Task<IReadOnlyList<PresetSummaryResponse>> GetPresetsAsync(CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<object?, List<PresetSummaryResponse>>(HttpMethod.Get, "api/v1/presets", null, cancellationToken)
        ?? [];

    internal async Task<PresetDocument> GetPresetAsync(string id, CancellationToken cancellationToken = default) =>
        await GetAndCachePresetAsync(id, cancellationToken);

    internal async Task<PresetDocument> GetActivePresetWithOfflineFallbackAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var preset = await SendAuthorizedAsync<object?, PresetDocument>(HttpMethod.Get, "api/v1/presets/active", null, cancellationToken)
                ?? throw new InvalidDataException("Server lieferte kein aktives Preset.");
            _storage.SavePresetCache(preset);
            return preset;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            if (Session is null || !IsAccessTokenLocallyValid(Session.AccessToken, out _))
            {
                throw new CashSlothServerException(503, "offline_session_expired", "Server ist offline und das lokale 12-Stunden-Token ist nicht mehr gültig.");
            }
            return _storage.LoadPresetCache()
                ?? throw new CashSlothServerException(503, "offline_preset_missing", "Server ist offline und es existiert noch kein Preset-Cache.");
        }
    }

    internal async Task<PresetWriteResponse> CreatePresetAsync(PresetDocument preset, CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<PresetDocument, PresetWriteResponse>(HttpMethod.Post, "api/v1/presets", preset, cancellationToken)
        ?? throw new InvalidDataException("Server lieferte keine Presetbestätigung.");

    internal async Task<PresetWriteResponse> UpdatePresetAsync(PresetDocument preset, CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<PresetDocument, PresetWriteResponse>(HttpMethod.Put, $"api/v1/presets/{Uri.EscapeDataString(preset.Id)}", preset, cancellationToken)
        ?? throw new InvalidDataException("Server lieferte keine Presetbestätigung.");

    internal Task SetActivePresetAsync(string id, CancellationToken cancellationToken = default) =>
        SendNoContentAsync(HttpMethod.Put, $"api/v1/presets/{Uri.EscapeDataString(id)}/active", new { }, cancellationToken);

    internal async Task<IReadOnlyList<AdminAccountResponse>> GetAccountsAsync(CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<object?, List<AdminAccountResponse>>(HttpMethod.Get, "api/v1/admin/accounts", null, cancellationToken)
        ?? [];

    internal Task SetAccountApprovalAsync(string id, bool approved, CancellationToken cancellationToken = default) =>
        SendNoContentAsync(HttpMethod.Put, $"api/v1/admin/accounts/{Uri.EscapeDataString(id)}/approval", new AdminAccountApprovalRequest(approved), cancellationToken);

    internal Task SetAccountRoleAsync(string id, string role, CancellationToken cancellationToken = default) =>
        SendNoContentAsync(HttpMethod.Put, $"api/v1/admin/accounts/{Uri.EscapeDataString(id)}/role", new AdminAccountRoleRequest(role), cancellationToken);

    internal Task SetAccountActiveAsync(string id, bool active, CancellationToken cancellationToken = default) =>
        SendNoContentAsync(HttpMethod.Put, $"api/v1/admin/accounts/{Uri.EscapeDataString(id)}/status", new AdminAccountStatusRequest(active), cancellationToken);

    internal async Task<string> ResetAccountPasswordAsync(string id, CancellationToken cancellationToken = default) =>
        (await SendAuthorizedAsync<object?, AdminPasswordResetResponse>(HttpMethod.Post, $"api/v1/admin/accounts/{Uri.EscapeDataString(id)}/password-reset", null, cancellationToken))?.TemporaryPassword
        ?? throw new InvalidDataException("Server lieferte kein temporäres Passwort.");

    internal async Task<ExchangeRateResponse> GetExchangeRatesAsync(CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<object?, ExchangeRateResponse>(HttpMethod.Get, "api/v1/reference/exchange-rates", null, cancellationToken)
        ?? throw new InvalidDataException("Server lieferte keine Wechselkurse.");

    internal async Task<TranslationResolveResponse> ResolveTranslationsAsync(
        TranslationResolveRequest request,
        CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<TranslationResolveRequest, TranslationResolveResponse>(
            HttpMethod.Post,
            "api/v1/reference/translations/resolve",
            request,
            cancellationToken)
        ?? throw new InvalidDataException("Server lieferte keine Übersetzungsantwort.");

    internal async Task<IReadOnlyList<EventSummaryResponse>> GetEventsAsync(bool includeOwnedDrafts = false, CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<object?, List<EventSummaryResponse>>(
            HttpMethod.Get,
            includeOwnedDrafts ? "api/v1/events?mine=true" : "api/v1/events",
            null,
            cancellationToken) ?? [];

    internal async Task<EventDetailResponse> GetEventAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<object?, EventDetailResponse>(HttpMethod.Get, $"api/v1/events/{eventId:N}", null, cancellationToken)
        ?? throw new InvalidDataException("Server lieferte keine Eventdaten.");

    internal async Task<EventDetailResponse> CreateEventDraftAsync(EventCreateRequest request, CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<EventCreateRequest, EventDetailResponse>(HttpMethod.Post, "api/v1/events", request, cancellationToken)
        ?? throw new InvalidDataException("Server lieferte keinen Evententwurf.");

    internal async Task<EventDetailResponse> UpdateEventDraftAsync(Guid eventId, EventUpdateDraftRequest request, CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<EventUpdateDraftRequest, EventDetailResponse>(HttpMethod.Put, $"api/v1/events/{eventId:N}", request, cancellationToken)
        ?? throw new InvalidDataException("Server lieferte keinen Evententwurf.");

    internal Task CancelEventDraftAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        SendNoContentAsync(HttpMethod.Delete, $"api/v1/events/{eventId:N}", new { }, cancellationToken);

    internal async Task<EventPublishResponse> PublishEventAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<object?, EventPublishResponse>(HttpMethod.Post, $"api/v1/events/{eventId:N}/publish", null, cancellationToken)
        ?? throw new InvalidDataException("Server lieferte keine Event-Startbestätigung.");

    internal async Task<EventMembershipResponse> JoinEventAsync(Guid eventId, EventJoinRequest request, CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<EventJoinRequest, EventMembershipResponse>(HttpMethod.Post, $"api/v1/events/{eventId:N}/join", request, cancellationToken)
        ?? throw new InvalidDataException("Server lieferte keine Eventmitgliedschaft.");

    internal async Task<EventMembershipResponse> ResumeEventHostAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<object?, EventMembershipResponse>(HttpMethod.Post, $"api/v1/events/{eventId:N}/host/resume", null, cancellationToken)
        ?? throw new InvalidDataException("Server lieferte keine Hostmitgliedschaft.");

    internal Task LeaveEventAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        SendNoContentAsync(HttpMethod.Post, $"api/v1/events/{eventId:N}/leave", new { }, cancellationToken);

    internal async Task<EventMemberResponse> RenameEventMemberAsync(Guid eventId, Guid memberId, string nickname, CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<EventMemberRenameRequest, EventMemberResponse>(
            HttpMethod.Put,
            $"api/v1/events/{eventId:N}/members/{memberId:N}/nickname",
            new EventMemberRenameRequest(nickname),
            cancellationToken)
        ?? throw new InvalidDataException("Server lieferte keine Mitgliedsbestätigung.");

    internal Task KickEventMemberAsync(Guid eventId, Guid memberId, CancellationToken cancellationToken = default) =>
        SendNoContentAsync(HttpMethod.Post, $"api/v1/events/{eventId:N}/members/{memberId:N}/kick", new { }, cancellationToken);

    internal async Task<EventHeartbeatResponse> SendEventHeartbeatAsync(Guid eventId, int pendingSaleCount, CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<EventHeartbeatRequest, EventHeartbeatResponse>(
            HttpMethod.Post,
            $"api/v1/events/{eventId:N}/heartbeat",
            new EventHeartbeatRequest(pendingSaleCount),
            cancellationToken)
        ?? throw new InvalidDataException("Server lieferte keine Heartbeat-Antwort.");

    internal async Task<EventSaleBatchResponse> UploadEventSalesAsync(Guid eventId, EventSaleBatchRequest request, CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<EventSaleBatchRequest, EventSaleBatchResponse>(
            HttpMethod.Post,
            $"api/v1/events/{eventId:N}/sales/batch",
            request,
            cancellationToken)
        ?? throw new InvalidDataException("Server lieferte keine Sync-Antwort.");

    internal async Task<EventStatisticsResponse> GetEventStatisticsAsync(Guid eventId, bool includeShowcase = false, CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<object?, EventStatisticsResponse>(
            HttpMethod.Get,
            $"api/v1/events/{eventId:N}/statistics?includeShowcase={includeShowcase.ToString().ToLowerInvariant()}",
            null,
            cancellationToken)
        ?? throw new InvalidDataException("Server lieferte keine Eventstatistik.");

    internal async Task<IReadOnlyList<EventSaleResponse>> GetEventSalesAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<object?, List<EventSaleResponse>>(HttpMethod.Get, $"api/v1/events/{eventId:N}/sales", null, cancellationToken) ?? [];

    internal async Task<EventCloseResponse> CloseEventAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<object?, EventCloseResponse>(HttpMethod.Post, $"api/v1/events/{eventId:N}/close", null, cancellationToken)
        ?? throw new InvalidDataException("Server lieferte keine Closing-Bestätigung.");

    internal async Task<EventFinalReportResponse> FinalizeEventAsync(Guid eventId, bool confirmIncomplete, CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<EventFinalizeRequest, EventFinalReportResponse>(
            HttpMethod.Post,
            $"api/v1/events/{eventId:N}/finalize",
            new EventFinalizeRequest(confirmIncomplete),
            cancellationToken)
        ?? throw new InvalidDataException("Server lieferte keinen Eventbericht.");

    internal async Task<EventFinalReportResponse> GetEventReportAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        await SendAuthorizedAsync<object?, EventFinalReportResponse>(HttpMethod.Get, $"api/v1/events/{eventId:N}/report", null, cancellationToken)
        ?? throw new InvalidDataException("Server lieferte keinen Eventbericht.");

    internal bool IsEventLeaseLocallyValid(CashSlothLocalEventSession eventSession, out ClaimsPrincipal? principal)
    {
        principal = null;
        var connection = Connection;
        if (connection?.DeviceId is not { } deviceId || eventSession.OfflineUntilUtc <= DateTimeOffset.UtcNow)
        {
            return false;
        }
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(connection.Trust.PublicKey), out _);
            var key = new ECDsaSecurityKey(ecdsa)
            {
                KeyId = connection.Trust.KeyId,
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
            };
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            principal = handler.ValidateToken(eventSession.OfflineLease, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = $"cashsloth-server:{connection.Trust.ServerId}",
                ValidateAudience = true,
                ValidAudience = "cashsloth-event-offline",
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            }, out _);
            return Guid.TryParse(principal.FindFirst("event_id")?.Value, out var eventId) && eventId == eventSession.Event.Id &&
                   Guid.TryParse(principal.FindFirst("member_id")?.Value, out var memberId) && memberId == eventSession.Membership.Id &&
                   Guid.TryParse(principal.FindFirst("device_id")?.Value, out var leaseDeviceId) && leaseDeviceId == deviceId &&
                   string.Equals(principal.FindFirst("preset_hash")?.Value, eventSession.Event.PresetHash, StringComparison.Ordinal) &&
                   string.Equals(principal.FindFirst("rules_hash")?.Value, HashEventRules(eventSession.Event.Rules), StringComparison.Ordinal);
        }
        catch
        {
            principal = null;
            return false;
        }
    }

    private static string HashEventRules(EventRulesDocument rules) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rules, EventJsonOptions)))).ToLowerInvariant();

    internal bool IsAccessTokenLocallyValid(string token, out ClaimsPrincipal? principal)
    {
        principal = null;
        var connection = Connection;
        if (connection is null)
        {
            return false;
        }
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(connection.Trust.PublicKey), out _);
            var key = new ECDsaSecurityKey(ecdsa)
            {
                KeyId = connection.Trust.KeyId,
                // This ECDsa instance is intentionally scoped to a single validation. Caching
                // its signature provider would retain the disposed key and make the next token
                // validation fail spuriously, triggering an immediate refresh/logout cycle.
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
            };
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = $"cashsloth-server:{connection.Trust.ServerId}",
                ValidateAudience = true,
                ValidAudience = "cashsloth-clients",
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
                NameClaimType = ClaimTypes.Name,
                RoleClaimType = ClaimTypes.Role
            }, out _);
            return connection.DeviceId is { } deviceId &&
                   Guid.TryParse(principal.FindFirst("device_id")?.Value, out var tokenDeviceId) &&
                   tokenDeviceId == deviceId;
        }
        catch (Exception exception) when (exception is SecurityTokenException or CryptographicException or FormatException)
        {
            principal = null;
            return false;
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private CashSlothClientSession? RestoreValidOfflineSession()
    {
        var session = _storage.LoadSession();
        if (session is not null && session.RefreshTokenExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return session;
        }
        _storage.ClearSession();
        return null;
    }

    private CashSlothClientSession SaveAndValidateSession(AuthTokenResponse response)
    {
        if (!IsAccessTokenLocallyValid(response.AccessToken, out var principal))
        {
            throw new CashSlothServerException(401, "invalid_server_token", "Server lieferte ein Token, das nicht zum gepinnten Serverschlüssel passt.");
        }
        if (principal?.FindFirst("sub")?.Value != response.User.Id)
        {
            throw new CashSlothServerException(401, "invalid_server_token", "Token und Benutzerantwort passen nicht zusammen.");
        }
        var session = new CashSlothClientSession(
            response.AccessToken,
            response.AccessTokenExpiresAtUtc,
            response.RefreshToken,
            response.RefreshTokenExpiresAtUtc,
            response.User);
        _storage.SaveSession(session);
        Session = session;
        SessionChanged?.Invoke(session.User);
        return session;
    }

    private async Task<PresetDocument> GetAndCachePresetAsync(string id, CancellationToken cancellationToken)
    {
        var preset = await SendAuthorizedAsync<object?, PresetDocument>(
            HttpMethod.Get,
            $"api/v1/presets/{Uri.EscapeDataString(id)}",
            null,
            cancellationToken)
            ?? throw new InvalidDataException("Server lieferte kein Preset.");
        if (preset.IsActive)
        {
            _storage.SavePresetCache(preset);
        }
        return preset;
    }

    private void ClearSession()
    {
        var hadSession = Session is not null || _storage.LoadSession() is not null;
        Session = null;
        _storage.ClearSession();
        if (hadSession)
        {
            SessionChanged?.Invoke(null);
        }
    }

    private void ClearSessionIfCurrent(string refreshToken)
    {
        if (Session is null || string.Equals(Session.RefreshToken, refreshToken, StringComparison.Ordinal))
        {
            ClearSession();
        }
    }

    private async Task<DeviceProof> CreateProofAsync(
        string purpose,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        var connection = RequireConnection(requireDevice: true);
        var challenge = await SendPublicAsync<DeviceChallengeRequest, DeviceChallengeResponse>(
            HttpMethod.Post,
            "api/v1/devices/challenge",
            new DeviceChallengeRequest(connection.DeviceId!.Value, purpose),
            cancellationToken);
        var proofText = $"cashsloth-device-proof-v1\n{purpose}\n{challenge.ChallengeId:N}\n{challenge.Nonce}\n{payloadHash}";
        using var key = _storage.LoadOrCreateDeviceKey();
        var signature = Base64UrlEncode(key.SignData(Encoding.UTF8.GetBytes(proofText), HashAlgorithmName.SHA256));
        return new DeviceProof(connection.DeviceId.Value, challenge.ChallengeId, signature);
    }

    private CashSlothClientConnection RequireConnection(bool requireDevice)
    {
        var connection = Connection
            ?? throw new CashSlothServerException(400, "server_trust_required", "Importiere zuerst die CashSloth-Trust-Datei.");
        if (requireDevice && connection.DeviceId is null)
        {
            throw new CashSlothServerException(400, "device_pairing_required", "Kopple dieses Gerät zuerst mit dem Server.");
        }
        return connection;
    }

    private async Task<T> GetPublicAsync<T>(string path, CancellationToken cancellationToken) =>
        await SendPublicAsync<object?, T>(HttpMethod.Get, path, null, cancellationToken);

    private async Task<TResponse> SendPublicAsync<TRequest, TResponse>(
        HttpMethod method,
        string path,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateMessage(method, path, request, includeAuthentication: false);
        using var response = await _httpClient.SendAsync(message, cancellationToken);
        return await ReadResponseAsync<TResponse>(response, cancellationToken);
    }

    private async Task<TResponse?> SendAuthorizedAsync<TRequest, TResponse>(
        HttpMethod method,
        string path,
        TRequest request,
        CancellationToken cancellationToken,
        bool allowNoContent = false)
    {
        if (Session is null || !IsAccessTokenLocallyValid(Session.AccessToken, out _))
        {
            await RefreshAsync(cancellationToken);
        }

        using (var message = CreateMessage(method, path, request, includeAuthentication: true))
        using (var response = await _httpClient.SendAsync(message, cancellationToken))
        {
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                if (allowNoContent && response.StatusCode == HttpStatusCode.NoContent)
                {
                    return default;
                }
                return await ReadResponseAsync<TResponse>(response, cancellationToken);
            }
        }

        // A locally valid token can still be rejected after a role/session change.
        // Refresh once to synchronize with the server's authoritative account state.
        await RefreshAsync(cancellationToken);
        using var retryMessage = CreateMessage(method, path, request, includeAuthentication: true);
        using var retryResponse = await _httpClient.SendAsync(retryMessage, cancellationToken);
        if (allowNoContent && retryResponse.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }
        return await ReadResponseAsync<TResponse>(retryResponse, cancellationToken);
    }

    private async Task SendNoContentAsync<TRequest>(HttpMethod method, string path, TRequest request, CancellationToken cancellationToken) =>
        await SendAuthorizedAsync<TRequest, object?>(method, path, request, cancellationToken, allowNoContent: true);

    private HttpRequestMessage CreateMessage<TRequest>(HttpMethod method, string path, TRequest request, bool includeAuthentication)
    {
        var connection = RequireConnection(requireDevice: false);
        var root = connection.Trust.HttpsUrl.TrimEnd('/') + "/";
        var message = new HttpRequestMessage(method, new Uri(new Uri(root), path.TrimStart('/')));
        if (request is not null)
        {
            message.Content = JsonContent.Create(request, options: _jsonOptions);
        }
        if (includeAuthentication)
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Session?.AccessToken);
        }
        return message;
    }

    private async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return default!;
            }
            return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken)
                ?? throw new InvalidDataException("Serverantwort ist leer.");
        }

        ApiError? error = null;
        try { error = await response.Content.ReadFromJsonAsync<ApiError>(_jsonOptions, cancellationToken); } catch { }
        throw new CashSlothServerException(
            (int)response.StatusCode,
            error?.Code ?? "server_request_failed",
            error?.Message ?? $"Server antwortete mit HTTP {(int)response.StatusCode}.");
    }

    private static string BuildPayloadHash(params string[] values) =>
        Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', values))));

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
