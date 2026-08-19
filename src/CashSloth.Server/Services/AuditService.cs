using CashSloth.Server.Data;

namespace CashSloth.Server.Services;

public sealed class AuditService(ServerDbContext db)
{
    public async Task WriteAsync(
        string actor,
        string action,
        string targetType,
        string? targetId = null,
        string? detail = null,
        string? traceId = null,
        CancellationToken cancellationToken = default)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Actor = Limit(actor, 200),
            Action = Limit(action, 100),
            TargetType = Limit(targetType, 100),
            TargetId = LimitNullable(targetId, 200),
            Detail = LimitNullable(detail, 1000),
            TraceId = LimitNullable(traceId, 100)
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Limit(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string? LimitNullable(string? value, int maxLength) =>
        value is null ? null : Limit(value, maxLength);
}
