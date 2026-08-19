using CashSloth.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CashSloth.Server.Data;

public static class DatabaseBootstrapper
{
    public static DbContextOptions<ServerDbContext> CreateOptions(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var connectionString = $"Data Source={databasePath};Mode=ReadWriteCreate;Cache=Shared;Default Timeout=5;Foreign Keys=True;Pooling=True";
        return new DbContextOptionsBuilder<ServerDbContext>()
            .UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(typeof(ServerDbContext).Assembly.FullName))
            .EnableDetailedErrors()
            .Options;
    }

    public static async Task InitializeAsync(ServerDbContext db, CancellationToken cancellationToken = default)
    {
        var migrations = db.Database.GetMigrations();
        if (migrations.Any())
        {
            await db.Database.MigrateAsync(cancellationToken);
        }
        else
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }

        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;", cancellationToken);

        foreach (var roleName in CashSlothRoles.All)
        {
            var normalizedName = roleName.ToUpperInvariant();
            var exists = await db.Roles.AnyAsync(
                role => role.NormalizedName == normalizedName,
                cancellationToken);
            if (!exists)
            {
                db.Roles.Add(new IdentityRole
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = roleName,
                    NormalizedName = normalizedName,
                    ConcurrencyStamp = Guid.NewGuid().ToString("N")
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
