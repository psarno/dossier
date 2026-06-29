using System.Text.Json;
using DossierApi.Models;
using DossierApi.Services;
using Microsoft.AspNetCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ─────────────────────────────────────────────────────────────
builder.Configuration.AddEnvironmentVariables();
AiProviderConfig.Validate(builder.Configuration);

var researchConfigPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "research.config.json");
if (!File.Exists(researchConfigPath))
    researchConfigPath = Path.Combine(Directory.GetCurrentDirectory(), "research.config.json");
var researchConfigJson = File.ReadAllText(researchConfigPath);
var researchConfig = JsonSerializer.Deserialize<ResearchConfig>(researchConfigJson,
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
    ?? throw new InvalidOperationException("Failed to load research.config.json");

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddSingleton(researchConfig);
builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddSingleton<ValidationService>();
builder.Services.AddSingleton<PipelineLog>();
builder.Services.AddHttpClient("anthropic");
builder.Services.AddHttpClient("openrouter");
builder.Services.AddSingleton<AnthropicClient>();
builder.Services.AddSingleton<OpenRouterClient>();
builder.Services.AddSingleton<IAiClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return (config["AI_PROVIDER"] ?? "").Trim().ToLowerInvariant() switch
    {
        "anthropic" => sp.GetRequiredService<AnthropicClient>(),
        "openrouter" => sp.GetRequiredService<OpenRouterClient>(),
        _ => throw new InvalidOperationException("Unsupported AI provider configuration.")
    };
});
builder.Services.AddSingleton<GraphExtractService>();
builder.Services.AddSingleton<SourceCatalogService>();
builder.Services.AddSingleton<GeneratedSourcesQueryService>();
builder.Services.AddSingleton<PipelineService>();

var corsOrigin = builder.Configuration["CORS_ORIGIN"] ?? "http://localhost:4200";
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(corsOrigin)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

// ── Top-level exception handler ───────────────────────────────────────────────
app.UseExceptionHandler(errApp =>
{
    errApp.Run(async ctx =>
    {
        var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
        var log = ctx.RequestServices.GetRequiredService<PipelineLog>();
        if (ex != null)
            log.Error($"Unhandled request exception [{ctx.Request.Method} {ctx.Request.Path}]: {ex.GetType().Name}: {ex.Message}");
        ctx.Response.StatusCode = 500;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new { error = ex?.Message ?? "Internal server error" });
    });
});

// Catch anything that escapes ASP.NET entirely
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    Console.Error.WriteLine($"[FATAL] Unhandled domain exception: {(e.ExceptionObject as Exception)?.Message ?? e.ExceptionObject?.ToString()}");

app.UseCors();

// ── Auth helper ───────────────────────────────────────────────────────────────
bool IsAuthorized(HttpContext ctx)
{
    var key = app.Configuration["ADMIN_KEY"];
    if (string.IsNullOrWhiteSpace(key)) return false;
    ctx.Request.Headers.TryGetValue("X-Admin-Key", out var provided);
    return provided == key;
}

// ── Public endpoints ──────────────────────────────────────────────────────────

app.MapGet("/api/sections", (DatabaseService db) =>
    Results.Ok(db.GetAllSections()));

app.MapGet("/api/sections/{slug}", (string slug, DatabaseService db) =>
{
    var section = db.GetSection(slug);
    return section is null ? Results.NotFound() : Results.Ok(section);
});

app.MapGet("/api/entries", (DatabaseService db) =>
    Results.Ok(db.GetAllEntries()));

app.MapGet("/api/entries/{slug}", (string slug, DatabaseService db) =>
{
    var entry = db.GetEntry(slug);
    return entry is null ? Results.NotFound() : Results.Ok(entry);
});

app.MapGet("/api/search", (string q, string? type, DatabaseService db) =>
{
    if (string.IsNullOrWhiteSpace(q)) return Results.BadRequest("q is required");
    var searchType = type ?? "all";
    if (searchType is not ("all" or "sections" or "entries"))
        return Results.BadRequest("type must be all, sections, or entries");
    return Results.Ok(db.Search(q, searchType));
});

app.MapGet("/api/metadata", (DatabaseService db) =>
    Results.Ok(db.GetMetadata()));

app.MapGet("/api/config", (ResearchConfig cfg) =>
    Results.Ok(cfg));

app.MapGet("/api/generated-sources", (GeneratedSourcesQueryService sources) =>
{
    var summary = sources.GetSummary();
    if (summary is null)
        return Results.NotFound(new { error = "Generated sources JSON not found. Run the pipeline first." });
    return Results.Json(summary);
});

app.MapGet("/api/generated-sources/groups/{key}", (string key, GeneratedSourcesQueryService sources) =>
{
    var group = sources.GetGroup(key);
    return group is null
        ? Results.NotFound(new { error = $"Generated sources group '{key}' was not found." })
        : Results.Json(group);
});

app.MapGet("/api/generated-sources/search", (string q, GeneratedSourcesQueryService sources) =>
{
    if (string.IsNullOrWhiteSpace(q))
        return Results.BadRequest(new { error = "q is required" });

    var groups = sources.Search(q);
    return groups is null
        ? Results.NotFound(new { error = "Generated sources JSON not found. Run the pipeline first." })
        : Results.Json(groups);
});

// ── Admin endpoints ───────────────────────────────────────────────────────────

app.MapGet("/api/admin/status", (HttpContext ctx, PipelineService pipeline) =>
{
    if (!IsAuthorized(ctx)) return Results.Unauthorized();
    return Results.Ok(pipeline.GetStatus());
});

app.MapGet("/api/admin/logs", (HttpContext ctx, PipelineLog log) =>
{
    if (!IsAuthorized(ctx)) return Results.Unauthorized();
    return Results.Ok(log.GetAll());
});

app.MapPost("/api/admin/upload", async (HttpContext ctx, PipelineService pipeline, PipelineLog log) =>
{
    if (!IsAuthorized(ctx)) return Results.Unauthorized();

    var form = await ctx.Request.ReadFormAsync();
    log.Info($"Upload request received: {form.Files.Count} file(s), fields=[{string.Join(", ", form.Select(kvp => kvp.Key))}]");
    foreach (var file in form.Files)
        log.Info($"Upload file: key='{file.Name}', filename='{file.FileName}', length={file.Length}, contentType='{file.ContentType}'");

    string? summaryContent = null, summaryFilename = null;
    string? namesContent = null, namesFilename = null;
    string? frameworkContent = null, frameworkFilename = null;

    if (form.Files.GetFile("summary") is IFormFile summaryFile)
    {
        using var sr = new StreamReader(summaryFile.OpenReadStream());
        summaryContent = await sr.ReadToEndAsync();
        summaryFilename = summaryFile.FileName;
    }
    else
    {
        log.Warn("Upload request did not include a 'summary' file.");
    }

    if (form.Files.GetFile("names") is IFormFile namesFile)
    {
        using var sr = new StreamReader(namesFile.OpenReadStream());
        namesContent = await sr.ReadToEndAsync();
        namesFilename = namesFile.FileName;
    }
    else
    {
        log.Warn("Upload request did not include a 'names' file.");
    }

    if (form.Files.GetFile("framework") is IFormFile frameworkFile)
    {
        using var sr = new StreamReader(frameworkFile.OpenReadStream());
        frameworkContent = await sr.ReadToEndAsync();
        frameworkFilename = frameworkFile.FileName;
    }
    else
    {
        log.Info("Upload request did not include a 'framework' file. Existing analytical framework will not be updated.");
    }

    bool hasSummary = summaryContent != null;
    bool hasNames = namesContent != null;
    bool hasFramework = frameworkContent != null;

    if (hasSummary != hasNames)
        return Results.BadRequest("If uploading summary or names, both files are required together.");

    if (!hasSummary && !hasFramework)
        return Results.BadRequest("Upload either both 'summary' and 'names', or a 'framework' file.");

    bool generateGraph = form["generate_graph"] == "true";

    // Fire and forget — CancellationToken.None so a dropped connection never aborts the pipeline
    _ = Task.Run(() => pipeline.RunAsync(
        summaryContent, summaryFilename,
        namesContent, namesFilename,
        frameworkContent, frameworkFilename,
        generateGraph,
        CancellationToken.None));

    return Results.Accepted("/api/admin/status", new { message = "Pipeline started" });
});

app.MapPost("/api/admin/cancel", (HttpContext ctx, PipelineService pipeline) =>
{
    if (!IsAuthorized(ctx)) return Results.Unauthorized();
    var cancelled = pipeline.Cancel();
    return cancelled
        ? Results.Ok(new { message = "Cancellation requested" })
        : Results.BadRequest(new { message = "No pipeline is currently running" });
});

app.MapPost("/api/admin/rollback", (HttpContext ctx, PipelineService pipeline) =>
{
    if (!IsAuthorized(ctx)) return Results.Unauthorized();
    var ok = pipeline.Rollback();
    return ok ? Results.Ok(new { message = "Rolled back to previous version" })
               : Results.NotFound(new { message = "No backup found" });
});

app.MapGet("/api/graph", (IConfiguration config) =>
{
    var dbPath = config["DB_PATH"] ?? "/data/dossier.db";
    var graphPath = Path.Combine(Path.GetDirectoryName(dbPath)!, "graph.json");
    if (!File.Exists(graphPath))
        return Results.NotFound(new { error = "Graph not yet generated. Run the pipeline first." });
    return Results.Content(File.ReadAllText(graphPath), "application/json");
});

// ── Health ────────────────────────────────────────────────────────────────────
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
