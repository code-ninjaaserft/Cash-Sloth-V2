using System.Diagnostics;
using System.Reflection;
using System.Security.Claims;
using CashSloth.Contracts;
using CashSloth.Server.Infrastructure;
using CashSloth.Server.Security;
using CashSloth.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace CashSloth.Server.Api;

public static class ServerApi
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }))
            .AllowAnonymous();

        var api = app.MapGroup("/api/v1");
        api.MapGet("/server/info", (ServerKeyService keys, ServerSettings settings) =>
            new ServerInfoResponse(
                keys.ServerId,
                Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0",
                settings.PublicUrl,
                keys.KeyId,
                DateTimeOffset.UtcNow)).AllowAnonymous();

        api.MapPost("/devices/pair", (DevicePairRequest request, DevicePairingService service, CancellationToken token) =>
            service.PairAsync(request, token)).AllowAnonymous().RequireRateLimiting("pairing");
        api.MapPost("/devices/challenge", (DeviceChallengeRequest request, DeviceProofService service, CancellationToken token) =>
            service.CreateChallengeAsync(request.DeviceId, request.Purpose, token)).AllowAnonymous().RequireRateLimiting("challenge");

        var auth = api.MapGroup("/auth");
        auth.MapPost("/register", (RegisterRequest request, AccountService service, CancellationToken token) =>
            service.RegisterAsync(request, token)).AllowAnonymous().RequireRateLimiting("register");
        auth.MapPost("/login", (LoginRequest request, AccountService service, CancellationToken token) =>
            service.LoginAsync(request, token)).AllowAnonymous().RequireRateLimiting("login");
        auth.MapPost("/refresh", (RefreshRequest request, AccountService service, CancellationToken token) =>
            service.RefreshAsync(request, token)).AllowAnonymous().RequireRateLimiting("refresh");
        auth.MapPost("/logout", async (LogoutRequest request, ClaimsPrincipal principal, AccountService service, CancellationToken token) =>
        {
            await service.LogoutAsync(principal.FindFirstValue("session_id") ?? string.Empty, request.RefreshToken, token);
            return Results.NoContent();
        }).RequireAuthorization("UserPlus");
        auth.MapGet("/me", (ClaimsPrincipal principal, AccountService service, CancellationToken token) =>
            service.GetProfileAsync(
                UserId(principal),
                GuidClaim(principal, "device_id"),
                GuidClaim(principal, "session_id"),
                token)).RequireAuthorization("UserPlus");
        auth.MapPost("/change-password", async (ChangePasswordRequest request, ClaimsPrincipal principal, AccountService service, CancellationToken token) =>
        {
            await service.ChangePasswordAsync(UserId(principal), request, token);
            return Results.NoContent();
        }).RequireAuthorization("UserPlus");

        var presets = api.MapGroup("/presets");
        presets.MapGet("/", (PresetService service, CancellationToken token) => service.ListAsync(token))
            .RequireAuthorization("UserPlus");
        presets.MapGet("/active", (PresetService service, CancellationToken token) => service.GetActiveAsync(token))
            .RequireAuthorization("UserPlus");
        presets.MapGet("/{id}", (string id, PresetService service, CancellationToken token) => service.GetAsync(id, token))
            .RequireAuthorization("UserPlus");
        presets.MapPost("/", (PresetDocument request, ClaimsPrincipal principal, PresetService service, CancellationToken token) =>
            service.CreateAsync(request, Actor(principal), token)).RequireAuthorization("CreatorPlus");
        presets.MapPut("/{id}", (string id, PresetDocument request, ClaimsPrincipal principal, PresetService service, CancellationToken token) =>
            service.UpdateAsync(id, request, Actor(principal), token)).RequireAuthorization("CreatorPlus");
        presets.MapDelete("/{id}", async (string id, ClaimsPrincipal principal, PresetService service, CancellationToken token) =>
        {
            await service.DeleteAsync(id, Actor(principal), token);
            return Results.NoContent();
        }).RequireAuthorization("Admin");
        presets.MapPut("/{id}/active", async (string id, ClaimsPrincipal principal, PresetService service, CancellationToken token) =>
        {
            await service.SetActiveAsync(id, Actor(principal), token);
            return Results.NoContent();
        }).RequireAuthorization("Admin");

        var reference = api.MapGroup("/reference").RequireAuthorization("UserPlus");
        reference.MapGet("/exchange-rates", (ReferenceDataService service, CancellationToken token) =>
            service.GetExchangeRatesAsync(token));
        reference.MapPost("/translations/resolve", (TranslationResolveRequest request, ReferenceDataService service, CancellationToken token) =>
            service.ResolveTranslationsAsync(request, token));

        var admin = api.MapGroup("/admin");
        admin.MapPut("/translations", async (TranslationUpsertRequest request, ClaimsPrincipal principal, ReferenceDataService service, CancellationToken token) =>
        {
            await service.UpsertTranslationAsync(request, Actor(principal), token);
            return Results.NoContent();
        }).RequireAuthorization("CreatorPlus");

        var accounts = admin.MapGroup("/accounts").RequireAuthorization("Admin");
        accounts.MapGet("/", (AccountService service, CancellationToken token) => service.ListAccountsAsync(token));
        accounts.MapPut("/{id}/approval", async (string id, AdminAccountApprovalRequest request, ClaimsPrincipal principal, AccountService service, CancellationToken token) =>
        {
            await service.SetApprovalAsync(id, request.IsApproved, Actor(principal), token);
            return Results.NoContent();
        });
        accounts.MapPut("/{id}/role", async (string id, AdminAccountRoleRequest request, ClaimsPrincipal principal, AccountService service, CancellationToken token) =>
        {
            await service.SetRoleAsync(id, request.Role, Actor(principal), token);
            return Results.NoContent();
        });
        accounts.MapPut("/{id}/status", async (string id, AdminAccountStatusRequest request, ClaimsPrincipal principal, AccountService service, CancellationToken token) =>
        {
            await service.SetActiveAsync(id, request.IsActive, Actor(principal), token);
            return Results.NoContent();
        });
        accounts.MapPost("/{id}/password-reset", async (string id, ClaimsPrincipal principal, AccountService service, CancellationToken token) =>
            new AdminPasswordResetResponse(await service.ResetPasswordAsync(id, Actor(principal), token)));

        var devices = admin.MapGroup("/devices").RequireAuthorization("Admin");
        devices.MapGet("/", (AdministrativeQueryService service, CancellationToken token) => service.ListDevicesAsync(token));
        devices.MapPut("/{id:guid}/name", async (Guid id, AdminDeviceRenameRequest request, ClaimsPrincipal principal, AdministrativeQueryService service, CancellationToken token) =>
        {
            await service.RenameDeviceAsync(id, request.Name, Actor(principal), token);
            return Results.NoContent();
        });
        devices.MapPut("/{id:guid}/status", async (Guid id, AdminDeviceStatusRequest request, ClaimsPrincipal principal, AdministrativeQueryService service, CancellationToken token) =>
        {
            await service.SetDeviceActiveAsync(id, request.IsActive, Actor(principal), token);
            return Results.NoContent();
        });
        admin.MapGet("/audit", (int? take, AdministrativeQueryService service, CancellationToken token) =>
            service.ListAuditAsync(take ?? 250, token)).RequireAuthorization("Admin");
    }

    private static string UserId(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.FindFirstValue("sub")
        ?? throw new ApiProblemException(401, "invalid_session", "Token enthält keine Benutzer-ID.");

    private static Guid GuidClaim(ClaimsPrincipal principal, string name) =>
        Guid.TryParse(principal.FindFirstValue(name), out var value)
            ? value
            : throw new ApiProblemException(401, "invalid_session", $"Token enthält kein gültiges {name}.");

    private static string Actor(ClaimsPrincipal principal) =>
        principal.Identity?.Name ?? principal.FindFirstValue("unique_name") ?? UserId(principal);
}
