using CashSloth.PresetApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
    options.SerializerOptions.WriteIndented = true;
});

var configuredDbPath = builder.Configuration["PRESET_DB_PATH"];
if (string.IsNullOrWhiteSpace(configuredDbPath))
{
    configuredDbPath = Path.Combine(AppContext.BaseDirectory, "data", "cashsloth.presets.sqlite3");
}

builder.Services.AddSingleton(new PresetRepository(configuredDbPath));

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    service = "CashSloth.PresetApi",
    status = "ok"
}));

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    utc = DateTimeOffset.UtcNow
}));

app.MapGet("/api/presets", (PresetRepository repository) =>
{
    var status = repository.TryReadStore(out var document, out var error);
    if (status == PresetRepoStatus.Success && document != null)
    {
        return Results.Ok(document);
    }

    return ToErrorResult(status, error, null);
});

app.MapGet("/api/presets/{presetId}", (string presetId, PresetRepository repository) =>
{
    var status = repository.TryReadPreset(presetId, out var preset, out var error);
    if (status == PresetRepoStatus.Success && preset != null)
    {
        return Results.Ok(preset);
    }

    return ToErrorResult(status, error, presetId);
});

app.MapPost("/api/presets/upload", (AssortmentPresetDocument preset, bool? setActive, PresetRepository repository) =>
{
    var shouldSetActive = setActive ?? true;
    var status = repository.TryUpsertPreset(preset, shouldSetActive, out var persistedPresetId, out var error);
    if (status == PresetRepoStatus.Success)
    {
        return Results.Ok(new
        {
            id = persistedPresetId,
            active = shouldSetActive
        });
    }

    return ToErrorResult(status, error, preset.Id);
});

app.MapPut("/api/presets/{presetId}", (string presetId, AssortmentPresetDocument preset, bool? setActive, PresetRepository repository) =>
{
    var shouldSetActive = setActive ?? false;
    var payload = preset with { Id = presetId };
    var status = repository.TryUpsertPreset(payload, shouldSetActive, out var persistedPresetId, out var error);
    if (status == PresetRepoStatus.Success)
    {
        return Results.Ok(new
        {
            id = persistedPresetId,
            active = shouldSetActive
        });
    }

    return ToErrorResult(status, error, presetId);
});

app.MapPut("/api/presets/{presetId}/active", (string presetId, PresetRepository repository) =>
{
    var status = repository.TrySetActivePreset(presetId, out var error);
    if (status == PresetRepoStatus.Success)
    {
        return Results.Ok(new
        {
            active_preset_id = PresetRepository.NormalizePresetId(presetId)
        });
    }

    return ToErrorResult(status, error, presetId);
});

app.MapDelete("/api/presets/{presetId}", (string presetId, PresetRepository repository) =>
{
    var status = repository.TryDeletePreset(presetId, out var error);
    if (status == PresetRepoStatus.Success)
    {
        return Results.Ok(new
        {
            deleted = PresetRepository.NormalizePresetId(presetId)
        });
    }

    return ToErrorResult(status, error, presetId);
});

app.Run();

static IResult ToErrorResult(PresetRepoStatus status, string? error, string? presetId)
{
    return status switch
    {
        PresetRepoStatus.InvalidInput => Results.BadRequest(new
        {
            error = error ?? "Invalid request."
        }),
        PresetRepoStatus.NotFound => Results.NotFound(new
        {
            error = error ?? $"Preset '{presetId}' does not exist."
        }),
        _ => Results.Problem(error ?? "Preset repository failed.", statusCode: StatusCodes.Status500InternalServerError)
    };
}
