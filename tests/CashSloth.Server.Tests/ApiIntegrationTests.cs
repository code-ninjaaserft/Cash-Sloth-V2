using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CashSloth.Contracts;
using CashSloth.Server.Api;
using CashSloth.Server.Data;
using CashSloth.Server.Infrastructure;
using CashSloth.Server.Security;
using CashSloth.Server.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CashSloth.Server.Tests;

public sealed class ApiIntegrationTests
{
    [Fact]
    public async Task Api_UsesUnifiedErrorsAndEnforcesPresetRoleMatrix()
    {
        var root = Path.Combine(Path.GetTempPath(), "CashSloth.Server.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var cloudflared = Path.Combine(root, "cloudflared.exe");
        await File.WriteAllBytesAsync(cloudflared, [0]);
        var settings = new ServerSettings
        {
            PublicUrl = "https://api.example.test",
            DataPath = root,
            CloudflaredPath = cloudflared
        };
        var paths = new ServerPaths(root);
        var settingsStore = new ServerSettingsStore(Path.Combine(root, "settings.json"));
        var app = ServerHostFactory.Build(settings, paths, settingsStore, new ServerLogBuffer());
        try
        {
            using (var scope = app.Services.CreateScope())
            {
                await DatabaseBootstrapper.InitializeAsync(scope.ServiceProvider.GetRequiredService<ServerDbContext>());
            }
            await app.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5080/") };

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("health/live")).StatusCode);
            var anonymous = await client.GetAsync("api/v1/presets");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            var error = await anonymous.Content.ReadFromJsonAsync<ApiError>();
            Assert.Equal("authentication_required", error?.Code);
            Assert.False(string.IsNullOrWhiteSpace(error?.TraceId));

            var userToken = await CreateTokenAsync(app.Services, "user", CashSlothRoles.User);
            var creatorToken = await CreateTokenAsync(app.Services, "creator", CashSlothRoles.Creator);
            var adminToken = await CreateTokenAsync(app.Services, "admin", CashSlothRoles.Admin);

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("api/v1/presets")).StatusCode);
            var forbiddenCreate = await client.PostAsJsonAsync("api/v1/presets", PresetServiceTests.CreatePreset("USER", "User"));
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenCreate.StatusCode);

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", creatorToken);
            var created = await client.PostAsJsonAsync("api/v1/presets", PresetServiceTests.CreatePreset("CREATOR", "Creator"));
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            var forbiddenDelete = await client.DeleteAsync("api/v1/presets/CREATOR");
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenDelete.StatusCode);

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("api/v1/presets/CREATOR")).StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            var fullRoot = Path.GetFullPath(root);
            var testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CashSloth.Server.Tests"));
            if (fullRoot.StartsWith(testRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullRoot))
            {
                Directory.Delete(fullRoot, recursive: true);
            }
        }
    }

    private static async Task<string> CreateTokenAsync(IServiceProvider services, string username, string role)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ServerUser>>();
        var user = new ServerUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = username,
            IsApproved = true,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            LockoutEnabled = true
        };
        Assert.True((await userManager.CreateAsync(user, "Very-Strong-Password-42!")).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(user, role)).Succeeded);
        var device = new Device
        {
            Id = Guid.NewGuid(),
            Name = username,
            PublicKey = Convert.ToBase64String([1, 2, 3]),
            PublicKeyFingerprint = Guid.NewGuid().ToString("N"),
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        var session = new LoginSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DeviceId = device.Id,
            RefreshTokenHash = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            LastRefreshedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1)
        };
        db.Devices.Add(device);
        db.LoginSessions.Add(session);
        await db.SaveChangesAsync();
        return scope.ServiceProvider.GetRequiredService<TokenService>()
            .CreateAccessToken(user, role, device.Id, session.Id).Token;
    }
}
