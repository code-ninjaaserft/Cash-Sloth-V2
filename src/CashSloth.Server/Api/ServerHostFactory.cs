using System.Net;
using System.Security.Claims;
using CashSloth.Contracts;
using CashSloth.Server.Data;
using CashSloth.Server.Infrastructure;
using CashSloth.Server.Security;
using CashSloth.Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;

namespace CashSloth.Server.Api;

public static class ServerHostFactory
{
    public static WebApplication Build(
        ServerSettings settings,
        ServerPaths paths,
        ServerSettingsStore settingsStore,
        ServerLogBuffer logs)
    {
        paths.EnsureDirectories();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(ServerHostFactory).Assembly.FullName,
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Production
        });

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 5080);
            options.Limits.MaxRequestBodySize = 1_048_576;
            options.AddServerHeader = false;
        });
        builder.Logging.ClearProviders();

        var keyService = new ServerKeyService(paths);
        var tokenService = new TokenService(keyService);
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(paths);
        builder.Services.AddSingleton(settingsStore);
        builder.Services.AddSingleton(logs);
        builder.Services.AddSingleton(keyService);
        builder.Services.AddSingleton(tokenService);
        builder.Services.AddSingleton<TunnelTokenStore>();
        builder.Services.AddSingleton<ReferenceDataLock>();
        builder.Services.AddDbContext<ServerDbContext>(options => options.UseSqlite(
            $"Data Source={paths.DatabasePath};Mode=ReadWriteCreate;Cache=Shared;Default Timeout=5;Foreign Keys=True;Pooling=True"));

        builder.Services
            .AddIdentityCore<ServerUser>(options =>
            {
                options.User.RequireUniqueEmail = false;
                options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._";
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ServerDbContext>()
            .AddDefaultTokenProviders();

        builder.Services.AddDataProtection()
            .SetApplicationName("CashSloth.Server")
            .PersistKeysToFileSystem(new DirectoryInfo(paths.DataProtectionKeysPath));

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = tokenService.CreateValidationParameters();
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = ValidateOnlineSessionAsync,
                    OnChallenge = context =>
                    {
                        context.HandleResponse();
                        return WriteErrorAsync(context.HttpContext, 401, "authentication_required", "Eine gültige Anmeldung ist erforderlich.");
                    },
                    OnForbidden = context =>
                        WriteErrorAsync(context.HttpContext, 403, "insufficient_role", "Für diese Aktion fehlt die erforderliche Rolle.")
                };
            });

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("UserPlus", policy => policy.RequireRole(CashSlothRoles.User, CashSlothRoles.Creator, CashSlothRoles.Admin));
            options.AddPolicy("CreatorPlus", policy => policy.RequireRole(CashSlothRoles.Creator, CashSlothRoles.Admin));
            options.AddPolicy("Admin", policy => policy.RequireRole(CashSlothRoles.Admin));
        });

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
                new ValueTask(WriteErrorAsync(context.HttpContext, 429, "rate_limit_exceeded", "Zu viele Anfragen. Bitte später erneut versuchen."));
            options.AddPolicy("pairing", context => FixedWindow(context, 5, TimeSpan.FromMinutes(1)));
            options.AddPolicy("challenge", context => FixedWindow(context, 30, TimeSpan.FromMinutes(1)));
            options.AddPolicy("register", context => FixedWindow(context, 5, TimeSpan.FromMinutes(10)));
            options.AddPolicy("login", context => FixedWindow(context, 10, TimeSpan.FromMinutes(5)));
            options.AddPolicy("refresh", context => FixedWindow(context, 30, TimeSpan.FromMinutes(5)));
        });

        builder.Services.AddHttpClient("frankfurter", client =>
        {
            client.BaseAddress = new Uri("https://api.frankfurter.dev/");
            client.Timeout = TimeSpan.FromSeconds(8);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CashSloth.Server/1.0");
        });
        builder.Services.AddHttpClient("public-health", client => client.Timeout = TimeSpan.FromSeconds(8));

        builder.Services.AddScoped<AuditService>();
        builder.Services.AddScoped<DeviceProofService>();
        builder.Services.AddScoped<DevicePairingService>();
        builder.Services.AddScoped<AccountService>();
        builder.Services.AddScoped<PresetService>();
        builder.Services.AddScoped<ReferenceDataService>();
        builder.Services.AddScoped<AdministrativeQueryService>();
        builder.Services.AddSingleton<BackupService>();

        var app = builder.Build();
        app.UseMiddleware<ApiExceptionMiddleware>();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseMiddleware<ForcedPasswordChangeMiddleware>();
        app.UseAuthorization();
        ServerApi.Map(app);
        return app;
    }

    private static RateLimitPartition<string> FixedWindow(HttpContext context, int permitLimit, TimeSpan window)
    {
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }

    private static async Task ValidateOnlineSessionAsync(TokenValidatedContext context)
    {
        var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.Principal?.FindFirstValue("sub");
        var deviceText = context.Principal?.FindFirstValue("device_id");
        var sessionText = context.Principal?.FindFirstValue("session_id");
        if (string.IsNullOrWhiteSpace(userId) ||
            !Guid.TryParse(deviceText, out var deviceId) ||
            !Guid.TryParse(sessionText, out var sessionId))
        {
            context.Fail("Token claims are incomplete.");
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<ServerDbContext>();
        var state = await db.LoginSessions.AsNoTracking()
            .Where(value => value.Id == sessionId && value.UserId == userId && value.DeviceId == deviceId)
            .Select(value => new
            {
                SessionActive = value.RevokedAtUtc == null && value.ExpiresAtUtc > DateTimeOffset.UtcNow,
                User = value.User,
                DeviceActive = value.Device.IsActive
            })
            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
        if (state is null || !state.SessionActive || !state.DeviceActive || !state.User.IsActive || !state.User.IsApproved)
        {
            context.Fail("Session, account, or device is inactive.");
            return;
        }

        var tokenRole = context.Principal!.FindFirstValue(ClaimTypes.Role);
        var currentRole = await (
            from userRole in db.UserRoles
            join role in db.Roles on userRole.RoleId equals role.Id
            where userRole.UserId == userId
            select role.Name).SingleOrDefaultAsync(context.HttpContext.RequestAborted);
        if (!string.Equals(tokenRole, currentRole, StringComparison.Ordinal))
        {
            context.Fail("Role has changed; refresh authentication.");
            return;
        }

        context.HttpContext.Items["CashSloth.User"] = state.User;
    }

    internal static Task WriteErrorAsync(HttpContext context, int status, string code, string message) =>
        WriteErrorAsync(context, status, code, message, null);

    internal static async Task WriteErrorAsync(
        HttpContext context,
        int status,
        string code,
        string message,
        IReadOnlyDictionary<string, string[]>? fields)
    {
        if (context.Response.HasStarted)
        {
            return;
        }
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new ApiError(code, message, fields, context.TraceIdentifier));
    }
}

public sealed class ApiExceptionMiddleware(RequestDelegate next, ServerLogBuffer logs)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ApiProblemException exception)
        {
            await ServerHostFactory.WriteErrorAsync(
                context,
                exception.StatusCode,
                exception.Code,
                exception.Message,
                exception.FieldErrors);
        }
        catch (BadHttpRequestException)
        {
            await ServerHostFactory.WriteErrorAsync(context, 400, "invalid_request", "Anfrage ist ungültig oder zu gross.");
        }
        catch (Exception exception)
        {
            logs.Add("API", $"{context.TraceIdentifier}: {exception.GetType().Name}: {exception.Message}");
            await ServerHostFactory.WriteErrorAsync(context, 503, "server_error", "Server konnte die Anfrage nicht verarbeiten.");
        }
    }
}

public sealed class ForcedPasswordChangeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Items["CashSloth.User"] is ServerUser { MustChangePassword: true } &&
            !context.Request.Path.Equals("/api/v1/auth/change-password") &&
            !context.Request.Path.Equals("/api/v1/auth/me") &&
            !context.Request.Path.Equals("/api/v1/auth/logout"))
        {
            await ServerHostFactory.WriteErrorAsync(context, 403, "password_change_required", "Das temporäre Passwort muss zuerst geändert werden.");
            return;
        }
        await next(context);
    }
}
