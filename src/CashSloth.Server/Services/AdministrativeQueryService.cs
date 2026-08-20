using CashSloth.Contracts;
using CashSloth.Server.Api;
using CashSloth.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace CashSloth.Server.Services;

public sealed class AdministrativeQueryService(ServerDbContext db, AuditService audit)
{
    public async Task<IReadOnlyList<AdminDeviceResponse>> ListDevicesAsync(CancellationToken cancellationToken = default) =>
        await db.Devices.AsNoTracking()
            .OrderBy(value => value.Name)
            .Select(value => new AdminDeviceResponse(
                value.Id,
                value.Name,
                value.IsActive,
                value.CreatedAtUtc,
                value.LastSeenAtUtc,
                value.PublicKeyFingerprint))
            .ToListAsync(cancellationToken);

    public async Task RenameDeviceAsync(Guid id, string name, string actor, CancellationToken cancellationToken = default)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is < 1 or > 100)
        {
            throw new ApiProblemException(400, "invalid_device_name", "Gerätename muss 1 bis 100 Zeichen lang sein.");
        }
        var device = await GetDeviceAsync(id, cancellationToken);
        device.Name = trimmed;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actor, "device.rename", "device", id.ToString("N"), trimmed, cancellationToken: cancellationToken);
    }

    public async Task SetDeviceActiveAsync(Guid id, bool active, string actor, CancellationToken cancellationToken = default)
    {
        var device = await GetDeviceAsync(id, cancellationToken);
        device.IsActive = active;
        if (!active)
        {
            var sessions = await db.LoginSessions
                .Where(value => value.DeviceId == id && value.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var session in sessions)
            {
                session.RevokedAtUtc = DateTimeOffset.UtcNow;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actor, active ? "device.enable" : "device.block", "device", id.ToString("N"), device.Name, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEventResponse>> ListAuditAsync(int take = 250, CancellationToken cancellationToken = default) =>
        await db.AuditEvents.AsNoTracking()
            .OrderByDescending(value => value.Id)
            .Take(Math.Clamp(take, 1, 1000))
            .Select(value => new AuditEventResponse(
                value.Id,
                value.CreatedAtUtc,
                value.Actor,
                value.Action,
                value.TargetType,
                value.TargetId,
                value.Detail,
                value.TraceId))
            .ToListAsync(cancellationToken);

    private async Task<Device> GetDeviceAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Devices.SingleOrDefaultAsync(value => value.Id == id, cancellationToken)
        ?? throw new ApiProblemException(404, "device_not_found", "Gerät wurde nicht gefunden.");
}
