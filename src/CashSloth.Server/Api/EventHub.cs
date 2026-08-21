using System.Security.Claims;
using CashSloth.Contracts;
using CashSloth.Server.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CashSloth.Server.Api;

[Authorize(Policy = "UserPlus")]
public sealed class EventHub(ServerDbContext db) : Hub
{
    public async Task JoinEvent(Guid eventId)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? Context.User?.FindFirstValue("sub");
        var deviceText = Context.User?.FindFirstValue("device_id");
        if (string.IsNullOrWhiteSpace(userId) || !Guid.TryParse(deviceText, out var deviceId))
        {
            throw new HubException("invalid_session");
        }

        var isMember = await db.EventMembers.AsNoTracking().AnyAsync(value =>
            value.EventId == eventId &&
            value.UserId == userId &&
            value.DeviceId == deviceId &&
            value.Status == CashSlothEventMemberStatus.Active,
            Context.ConnectionAborted);
        if (!isMember)
        {
            throw new HubException("event_membership_required");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(eventId), Context.ConnectionAborted);
    }

    internal static string GroupName(Guid eventId) => $"event:{eventId:N}";
}
