using CashSloth.Contracts;
using CashSloth.Server.Data;
using CashSloth.Server.Infrastructure;
using CashSloth.Server.Security;
using CashSloth.Server.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CashSloth.Server.Tests;

internal sealed class TestServerContext : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private TestServerContext(string root, ServiceProvider provider)
    {
        Root = root;
        Paths = new ServerPaths(root);
        _provider = provider;
    }

    internal string Root { get; }
    internal ServerPaths Paths { get; }
    internal IServiceScope CreateScope() => _provider.CreateScope();
    internal T GetRequiredService<T>() where T : notnull => _provider.GetRequiredService<T>();

    internal static async Task<TestServerContext> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "CashSloth.Server.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var paths = new ServerPaths(root);
        paths.EnsureDirectories();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<ServerDbContext>(options => options.UseSqlite(
            $"Data Source={paths.DatabasePath};Mode=ReadWriteCreate;Cache=Shared;Default Timeout=5;Foreign Keys=True;Pooling=False"));
        services.AddIdentityCore<ServerUser>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ServerDbContext>()
            .AddDefaultTokenProviders();
        services.AddSingleton(paths);
        services.AddSingleton<ServerKeyService>();
        services.AddSingleton<TokenService>();
        services.AddScoped<AuditService>();
        services.AddScoped<DeviceProofService>();
        services.AddScoped<DevicePairingService>();
        services.AddScoped<AccountService>();
        services.AddScoped<PresetService>();
        services.AddScoped<AdministrativeQueryService>();

        var provider = services.BuildServiceProvider();
        var context = new TestServerContext(root, provider);
        using var scope = context.CreateScope();
        await DatabaseBootstrapper.InitializeAsync(scope.ServiceProvider.GetRequiredService<ServerDbContext>());
        return context;
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var fullRoot = Path.GetFullPath(Root);
        var testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CashSloth.Server.Tests"));
        if (fullRoot.StartsWith(testRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullRoot))
        {
            Directory.Delete(fullRoot, recursive: true);
        }
    }

    internal static string RoleName(CashSlothRole role) => role.ToString();
}
