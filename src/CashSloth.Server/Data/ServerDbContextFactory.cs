using CashSloth.Server.Infrastructure;
using Microsoft.EntityFrameworkCore.Design;

namespace CashSloth.Server.Data;

public sealed class ServerDbContextFactory : IDesignTimeDbContextFactory<ServerDbContext>
{
    public ServerDbContext CreateDbContext(string[] args)
    {
        var path = Path.Combine(Path.GetTempPath(), "cashsloth-server-design.sqlite3");
        return new ServerDbContext(DatabaseBootstrapper.CreateOptions(path));
    }
}
