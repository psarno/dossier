using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using DossierApi.Models;

namespace DossierApi.Services;

public class PipelineService
{
    private const string SectionMetadataPromptVersion = "section-metadata-v1";

    private readonly DatabaseService _db;
    private readonly IAiClient _ai;
    private readonly ValidationService _validator;
    private readonly GraphExtractService _graphExtract;
    private readonly SourceCatalogService _sourceCatalog;
    private readonly IConfiguration _config;
    private readonly ResearchConfig _researchConfig;
    private readonly ILogger<PipelineService> _logger;
    private readonly PipelineLog _pipelineLog;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private PipelineStatus _status = new();
    private CancellationTokenSource? _cts;

    public PipelineService(
        DatabaseService db,
        IAiClient ai,
        ValidationService validator,
        GraphExtractService graphExtract,
        SourceCatalogService sourceCatalog,
        IConfiguration config,
        ResearchConfig researchConfig,
        ILogger<PipelineService> logger,
        PipelineLog pipelineLog)
    {
        _db = db;
        _ai = ai;
        _validator = validator;
        _graphExtract = graphExtract;
        _sourceCatalog = sourceCatalog;
        _config = config;
        _researchConfig = researchConfig;
        _logger = logger;
        _pipelineLog = pipelineLog;
    }

    public PipelineStatus GetStatus()
    {
        var meta = _db.GetMetadata();
        _status.CurrentSummaryVersion = meta.GetValueOrDefault("summary_version");
        _status.CurrentNamesVersion = meta.GetValueOrDefault("names_version");
        _status.CurrentFrameworkVersion = meta.GetValueOrDefault("framework_version");
        _status.BuiltAt = meta.GetValueOrDefault("built_at");
        return _status;
    }

    public bool Cancel()
    {
        var cts = _cts;
        if (cts == null || !_status.IsRunning) return false;
        cts.Cancel();
        _pipelineLog.Warn("Cancellation requested by user.");
        return true;
    }

    public async Task<PipelineResult> RunAsync(
        string? summaryContent, string? summaryFilename,
        string? namesContent, string? namesFilename,
        string? frameworkContent, string? frameworkFilename,
        bool generateGraph = false,
        CancellationToken ct = default)
    {
        if (!await _lock.WaitAsync(0))
            return new PipelineResult { Success = false, Errors = ["Pipeline already running"] };

        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var effectiveCt = _cts.Token;

        _pipelineLog.Clear();
        _status.IsRunning = true;
        var sw = Stopwatch.StartNew();
        var result = new PipelineResult();
        _pipelineLog.Info("Pipeline started");
        _pipelineLog.Info($"AI provider: {_ai.ProviderName} ({_ai.ModelName})");

        string dbPath = _config["DB_PATH"] ?? "/data/dossier.db";
        string stagingPath = Path.ChangeExtension(dbPath, ".db").Replace(".db", "_staging.db");
        string backupPath = dbPath + ".bak";
        string markdownDir = Path.Combine(Path.GetDirectoryName(dbPath)!, "markdown");
        string dataDir = Path.GetDirectoryName(dbPath)!;
        string stagingSourcesJson = Path.Combine(dataDir, "sources.generated_staging.json");
        string stagingSourcesMd = Path.Combine(dataDir, "sources.generated_staging.md");
        string liveSourcesJson = Path.Combine(dataDir, "sources.generated.json");
        string liveSourcesMd = Path.Combine(dataDir, "sources.generated.md");
        bool summaryUpdated = summaryContent != null;
        bool namesUpdated = namesContent != null;
        bool frameworkUpdated = frameworkContent != null;
        bool summaryOrNamesUpdated = summaryUpdated || namesUpdated;
        var existingMetadata = _db.GetMetadata();

        try
        {
            Directory.CreateDirectory(markdownDir);

            // Step 1 — Archive markdown files
            string timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            if (summaryContent != null && summaryFilename != null)
            {
                var archivePath = Path.Combine(markdownDir, $"{Path.GetFileNameWithoutExtension(summaryFilename)}_{timestamp}.md");
                await File.WriteAllTextAsync(archivePath, summaryContent, effectiveCt);
                _pipelineLog.Info($"Archived summary to {Path.GetFileName(archivePath)}");
            }
            if (namesContent != null && namesFilename != null)
            {
                var archivePath = Path.Combine(markdownDir, $"{Path.GetFileNameWithoutExtension(namesFilename)}_{timestamp}.md");
                await File.WriteAllTextAsync(archivePath, namesContent, effectiveCt);
                _pipelineLog.Info($"Archived names index to {Path.GetFileName(archivePath)}");
            }
            if (frameworkContent != null && frameworkFilename != null)
            {
                var archivePath = Path.Combine(markdownDir, $"{Path.GetFileNameWithoutExtension(frameworkFilename)}_{timestamp}.md");
                await File.WriteAllTextAsync(archivePath, frameworkContent, effectiveCt);
                _pipelineLog.Info($"Archived analytical framework to {Path.GetFileName(archivePath)}");
            }

            // Step 2 — Parse summary
            var summarySections = new List<Section>();
            string summaryVersion = existingMetadata.GetValueOrDefault("summary_version", "unknown");
            if (summaryUpdated)
            {
                summaryVersion = MarkdownParser.ExtractVersion(summaryFilename ?? "");
                _pipelineLog.Info($"Parsing summary {summaryVersion} ({summaryContent!.Length:N0} chars)…");
                summarySections = MarkdownParser.ParseSummary(summaryContent!, summaryVersion,
                    _researchConfig.Tags.Select(t => t.Key));
                _logger.LogInformation("Parsed {Count} sections from summary {Version}", summarySections.Count, summaryVersion);
                _pipelineLog.Info($"Parsed {summarySections.Count} sections from summary {summaryVersion}");
            }
            else
            {
                summarySections = _db.GetSectionsByDocType("summary");
                summaryFilename = existingMetadata.GetValueOrDefault("summary_filename");
                _pipelineLog.Info($"Reusing existing summary data ({summarySections.Count} sections, version {summaryVersion})");
            }

            // Step 3 — Parse names index
            var entries = new List<Entry>();
            string namesVersion = existingMetadata.GetValueOrDefault("names_version", "unknown");
            if (namesUpdated)
            {
                namesVersion = MarkdownParser.ExtractVersion(namesFilename ?? "");
                _pipelineLog.Info($"Parsing names index {namesVersion} ({namesContent!.Length:N0} chars)…");
                entries = MarkdownParser.ParseNamesIndex(namesContent!, namesVersion);
                _logger.LogInformation("Parsed {Count} entries from names index {Version}", entries.Count, namesVersion);
                _pipelineLog.Info($"Parsed {entries.Count} entries from names index {namesVersion}");
            }
            else
            {
                entries = _db.GetEntriesDetailed();
                namesFilename = existingMetadata.GetValueOrDefault("names_filename");
                _pipelineLog.Info($"Reusing existing names index data ({entries.Count} entries, version {namesVersion})");
            }

            // Step 3.5 — Parse analytical framework
            var frameworkSections = new List<Section>();
            string frameworkVersion = existingMetadata.GetValueOrDefault("framework_version", "unknown");
            if (frameworkUpdated)
            {
                frameworkVersion = MarkdownParser.ExtractVersion(frameworkFilename ?? "");
                _pipelineLog.Info($"Parsing analytical framework {frameworkVersion} ({frameworkContent!.Length:N0} chars)…");
                var frameworkSection = MarkdownParser.ParseAnalyticalFramework(frameworkContent!, frameworkVersion);
                if (frameworkSection != null)
                    frameworkSections.Add(frameworkSection);
                _logger.LogInformation("Parsed {Count} sections from analytical framework {Version}", frameworkSections.Count, frameworkVersion);
                _pipelineLog.Info($"Parsed {frameworkSections.Count} sections from analytical framework {frameworkVersion}");
            }
            else
            {
                frameworkSections = _db.GetSectionsByDocType("analytical_framework");
                frameworkFilename = existingMetadata.GetValueOrDefault("framework_filename");
                _pipelineLog.Info($"Reusing existing analytical framework data ({frameworkSections.Count} sections, version {frameworkVersion})");
            }

            // Step 4 — AI extraction (slug + title cleanup + tags)
            var aiSectionCache = new ConcurrentDictionary<string, AiSectionCacheEntry>(
                _db.GetAiSectionCacheEntries().ToDictionary(BuildCacheKey, StringComparer.Ordinal),
                StringComparer.Ordinal);
            if (summaryUpdated)
            {
                _pipelineLog.Info($"Starting AI enrichment for {summarySections.Count} sections (5 concurrent)…");
                summarySections = await EnrichSectionsWithAi(summarySections, aiSectionCache, effectiveCt);
                _pipelineLog.Info("AI enrichment complete");
            }

            // Deduplicate slugs (in case AI produces duplicates)
            summarySections = DeduplicateSlugs(summarySections);
            entries = DeduplicateEntrySlugs(entries);

            // Resolve entry sectionRefs against final section slugs.
            // Parser stores candidate slugs derived from the **Summary sections:** field;
            // here we match each candidate to an actual section slug (exact, then contains)
            // and drop any that don't resolve — prevents broken links.
            if (summarySections.Count > 0 && entries.Count > 0)
            {
                entries = ResolveEntryRefs(entries, summarySections);
                _pipelineLog.Info("Resolved section refs for entries");
            }

            var sections = new List<Section>(summarySections.Count + frameworkSections.Count);
            sections.AddRange(summarySections);
            sections.AddRange(frameworkSections);

            // Step 4.5 — Deterministic generated sources artifacts
            GeneratedSourcesReport? sourcesReport = null;
            if (summaryOrNamesUpdated)
            {
                _pipelineLog.Info("Extracting generated sources from summary and names…");
                sourcesReport = _sourceCatalog.Generate(summaryContent, summaryVersion, namesContent, namesVersion);
                _sourceCatalog.WriteArtifacts(dataDir, sourcesReport, "sources.generated_staging");
                _pipelineLog.Info($"Generated sources: {sourcesReport.Items.Count} items, {sourcesReport.Review.Count} review fragments");
                if (sourcesReport.Review.Count > 0)
                {
                    result.Warnings.Add($"Generated sources has {sourcesReport.Review.Count} fragment(s) needing review.");
                    _pipelineLog.Warn($"Generated sources review queue: {sourcesReport.Review.Count} fragment(s)");
                }
            }
            else
            {
                _pipelineLog.Info("Summary and names unchanged — preserving existing generated sources artifacts.");
                if (File.Exists(liveSourcesJson))
                    File.Copy(liveSourcesJson, stagingSourcesJson, overwrite: true);
                if (File.Exists(liveSourcesMd))
                    File.Copy(liveSourcesMd, stagingSourcesMd, overwrite: true);

                if (!File.Exists(stagingSourcesJson) || !File.Exists(stagingSourcesMd))
                {
                    sourcesReport = _sourceCatalog.Generate(null, summaryVersion, null, namesVersion);
                    _sourceCatalog.WriteArtifacts(dataDir, sourcesReport, "sources.generated_staging");
                    _pipelineLog.Warn("Generated sources artifacts were missing; created empty staging artifacts.");
                }
            }

            // Step 5 — Write staging DB
            _pipelineLog.Info("Writing staging database…");
            if (File.Exists(stagingPath)) File.Delete(stagingPath);

            // Use a simple rollback journal for the staging DB so the swap operates on a
            // single concrete file instead of depending on WAL sidecars during handoff.
            using (var stagingConn = _db.OpenConnection(stagingPath, useWal: false))
            {
                _db.InitSchema(stagingConn);

                using var tx = stagingConn.BeginTransaction();
                foreach (var s in sections)
                    _db.InsertSection(stagingConn, s);
                foreach (var e in entries)
                    _db.InsertEntry(stagingConn, e);
                foreach (var cacheEntry in aiSectionCache.Values)
                    _db.InsertAiSectionCacheEntry(stagingConn, cacheEntry);

                _db.SetMetadata(stagingConn, "summary_version", summaryVersion);
                _db.SetMetadata(stagingConn, "names_version", namesVersion);
                _db.SetMetadata(stagingConn, "built_at", DateTime.UtcNow.ToString("O"));
                if (summaryFilename != null)
                    _db.SetMetadata(stagingConn, "summary_filename", summaryFilename);
                if (namesFilename != null)
                    _db.SetMetadata(stagingConn, "names_filename", namesFilename);
                _db.SetMetadata(stagingConn, "framework_version", frameworkVersion);
                if (frameworkFilename != null)
                    _db.SetMetadata(stagingConn, "framework_filename", frameworkFilename);
                _db.SetMetadata(stagingConn, "sources_json_filename", "sources.generated.json");
                _db.SetMetadata(stagingConn, "sources_markdown_filename", "sources.generated.md");
                _db.SetMetadata(stagingConn, "sources_item_count", sourcesReport?.Items.Count.ToString() ?? existingMetadata.GetValueOrDefault("sources_item_count", "0"));
                _db.SetMetadata(stagingConn, "sources_review_count", sourcesReport?.Review.Count.ToString() ?? existingMetadata.GetValueOrDefault("sources_review_count", "0"));
                _db.SetMetadata(stagingConn, "section_metadata_prompt_version", SectionMetadataPromptVersion);

                _db.PopulateFts(stagingConn);
                tx.Commit();

                // Step 6 — Validate staging DB
                _pipelineLog.Info("Validating staging database…");
                var (valid, errors) = _validator.Validate(stagingConn);
                result.Errors = errors;
                result.SectionCount = sections.Count;
                result.EntryCount = entries.Count;
                result.SummaryVersion = summaryVersion;
                result.NamesVersion = namesVersion;
                result.FrameworkVersion = frameworkVersion;

                if (!valid)
                {
                    foreach (var err in errors)
                        _pipelineLog.Error($"Validation: {err}");
                    result.Success = false;
                    return result;
                }
                _pipelineLog.Info($"Validation passed — {sections.Count} sections, {entries.Count} entries");
            }

            // Step 7 — Atomic swap
            _pipelineLog.Info("Swapping staging database to live…");
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var ext in new[] { "-wal", "-shm" })
            {
                var f = stagingPath + ext;
                if (File.Exists(f)) File.Delete(f);
            }
            if (File.Exists(dbPath))
            {
                File.Copy(dbPath, backupPath, overwrite: true);
                foreach (var ext in new[] { "-wal", "-shm" })
                {
                    var f = dbPath + ext;
                    if (File.Exists(f)) File.Delete(f);
                }
            }
            File.Move(stagingPath, dbPath, overwrite: true);
            File.Move(stagingSourcesJson, liveSourcesJson, overwrite: true);
            File.Move(stagingSourcesMd, liveSourcesMd, overwrite: true);

            // Force all pooled connections closed so next queries open fresh handles to the new file
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            result.Success = true;
            _logger.LogInformation("Pipeline complete: {Sections} sections, {Entries} entries in {Elapsed:F1}s",
                sections.Count, entries.Count, sw.Elapsed.TotalSeconds);
            _pipelineLog.Info($"Pipeline complete: {sections.Count} sections, {entries.Count} entries in {sw.Elapsed.TotalSeconds:F1}s");

            // Step 8 — Graph extraction (non-fatal: main pipeline already succeeded)
            if (generateGraph && summaryUpdated && namesUpdated)
            {
                try
                {
                    _pipelineLog.Info("Starting graph extraction…");
                    await _graphExtract.ExtractAndSaveAsync(summaryContent, namesContent, effectiveCt);
                }
                catch (Exception ex)
                {
                    _pipelineLog.Warn($"Graph extraction failed (non-fatal): {ex.Message}");
                }
            }
            else if (generateGraph)
            {
                _pipelineLog.Warn("Graph extraction requested, but skipped because summary and names were not both uploaded in this run.");
            }
            else
            {
                _pipelineLog.Info("Graph extraction skipped (not requested).");
            }
        }
        catch (OperationCanceledException)
        {
            _pipelineLog.Warn("Pipeline cancelled.");
            result.Success = false;
            result.Errors.Add("Pipeline was cancelled.");
            if (File.Exists(stagingPath))
                try { File.Delete(stagingPath); } catch { /* best effort */ }
            if (File.Exists(stagingSourcesJson))
                try { File.Delete(stagingSourcesJson); } catch { /* best effort */ }
            if (File.Exists(stagingSourcesMd))
                try { File.Delete(stagingSourcesMd); } catch { /* best effort */ }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline failed");
            _pipelineLog.Error($"Pipeline failed: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                _pipelineLog.Error($"  Inner: {ex.InnerException.Message}");
            result.Success = false;
            result.Errors.Add($"Unhandled error: {ex.Message}");
            if (File.Exists(stagingPath))
                try { File.Delete(stagingPath); } catch { /* best effort */ }
            if (File.Exists(stagingSourcesJson))
                try { File.Delete(stagingSourcesJson); } catch { /* best effort */ }
            if (File.Exists(stagingSourcesMd))
                try { File.Delete(stagingSourcesMd); } catch { /* best effort */ }
        }
        finally
        {
            result.ElapsedSeconds = sw.Elapsed.TotalSeconds;
            _status.IsRunning = false;
            _status.LastRunAt = DateTime.UtcNow;
            _status.LastResult = result;
            _lock.Release();
            _cts?.Dispose();
            _cts = null;
        }

        return result;
    }

    public bool Rollback()
    {
        string dbPath = _config["DB_PATH"] ?? "/data/dossier.db";
        string backupPath = dbPath + ".bak";

        if (!File.Exists(backupPath)) return false;

        File.Copy(backupPath, dbPath, overwrite: true);
        _logger.LogInformation("Rolled back to backup DB");
        return true;
    }

    private async Task<List<Section>> EnrichSectionsWithAi(
        List<Section> sections,
        ConcurrentDictionary<string, AiSectionCacheEntry> aiSectionCache,
        CancellationToken ct)
    {
        int total = sections.Count;
        int completed = 0;
        var semaphore = new SemaphoreSlim(5, 5);
        var tasks = sections.Select(async section =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var sectionHash = ComputeSectionHash(section);
                var cacheKey = BuildCacheKey(sectionHash, _ai.ProviderName, _ai.ModelName, SectionMetadataPromptVersion);
                if (aiSectionCache.TryGetValue(cacheKey, out var cached))
                {
                    ApplyCachedSectionMetadata(section, cached);
                    int cachedN = Interlocked.Increment(ref completed);
                    _pipelineLog.Info($"AI [{cachedN}/{total}] cache hit: {section.Title}");
                    return section;
                }

                var extraction = await _ai.ExtractSectionMetadata(section.Title, section.Body, ct);
                int n = Interlocked.Increment(ref completed);
                if (extraction != null)
                {
                    if (!string.IsNullOrWhiteSpace(extraction.Slug))
                        section.Slug = extraction.Slug;
                    if (!string.IsNullOrWhiteSpace(extraction.Title))
                        section.Title = extraction.Title;
                    if (extraction.TagsPresent.Count > 0)
                        section.TagsPresent = JsonSerializer.Serialize(extraction.TagsPresent);
                    aiSectionCache[cacheKey] = new AiSectionCacheEntry
                    {
                        SectionHash = sectionHash,
                        ProviderName = _ai.ProviderName,
                        ModelName = _ai.ModelName,
                        PromptVersion = SectionMetadataPromptVersion,
                        Slug = section.Slug,
                        Title = section.Title,
                        TagsPresent = section.TagsPresent,
                        CachedAt = DateTime.UtcNow.ToString("O")
                    };
                    _pipelineLog.Info($"AI [{n}/{total}] {section.Title}");
                }
                else
                {
                    _logger.LogWarning("AI extraction failed for section: {Title} — using parsed fallback", section.Title);
                    _pipelineLog.Warn($"AI [{n}/{total}] fallback (no extraction): {section.Title}");
                }
                return section;
            }
            finally
            {
                semaphore.Release();
            }
        });
        return [.. await Task.WhenAll(tasks)];
    }

    private static void ApplyCachedSectionMetadata(Section section, AiSectionCacheEntry cached)
    {
        if (!string.IsNullOrWhiteSpace(cached.Slug))
            section.Slug = cached.Slug;
        if (!string.IsNullOrWhiteSpace(cached.Title))
            section.Title = cached.Title;
        if (!string.IsNullOrWhiteSpace(cached.TagsPresent))
            section.TagsPresent = cached.TagsPresent;
    }

    private static string ComputeSectionHash(Section section)
    {
        var source = $"{section.SourceHeading}\n---\n{section.Body}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string BuildCacheKey(AiSectionCacheEntry entry) =>
        BuildCacheKey(entry.SectionHash, entry.ProviderName, entry.ModelName, entry.PromptVersion);

    private static string BuildCacheKey(string sectionHash, string providerName, string modelName, string promptVersion) =>
        $"{sectionHash}|{providerName}|{modelName}|{promptVersion}";

    private static List<Section> DeduplicateSlugs(List<Section> sections)
    {
        var seen = new Dictionary<string, int>();
        foreach (var s in sections)
        {
            if (seen.TryGetValue(s.Slug, out int count))
            {
                seen[s.Slug] = count + 1;
                s.Slug = $"{s.Slug}-{count + 1}";
            }
            else
            {
                seen[s.Slug] = 1;
            }
        }
        return sections;
    }

    private static List<Entry> DeduplicateEntrySlugs(List<Entry> entries)
    {
        var seen = new Dictionary<string, int>();
        foreach (var e in entries)
        {
            if (seen.TryGetValue(e.Slug, out int count))
            {
                seen[e.Slug] = count + 1;
                e.Slug = $"{e.Slug}-{count + 1}";
            }
            else
            {
                seen[e.Slug] = 1;
            }
        }
        return entries;
    }

    // Resolve each entry's sectionRefs (candidate slugs from **Summary sections:** parsing)
    // to actual section slugs. Strategy:
    //   1. Exact slug match
    //   2. Find section whose slug contains the candidate slug as a substring
    //   3. Drop unresolvable refs (better than a broken link)
    private static List<Entry> ResolveEntryRefs(List<Entry> entries, List<Section> sections)
    {
        var slugSet = new HashSet<string>(sections.Select(s => s.Slug), StringComparer.OrdinalIgnoreCase);
        var allSlugs = sections.Select(s => s.Slug).ToList();

        foreach (var entry in entries)
        {
            List<string> parsed;
            try { parsed = JsonSerializer.Deserialize<List<string>>(entry.SectionRefs) ?? []; }
            catch { parsed = []; }

            var resolved = new List<string>();
            foreach (var candidate in parsed)
            {
                if (slugSet.Contains(candidate))
                {
                    resolved.Add(candidate);
                }
                else
                {
                    // Prefer shortest containing slug to avoid overly broad matches
                    var hit = allSlugs
                        .Where(s => s.Contains(candidate, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(s => s.Length)
                        .FirstOrDefault();
                    if (hit != null)
                        resolved.Add(hit);
                    // Otherwise drop — unresolvable ref becomes no link rather than a broken link
                }
            }

            var items = string.Join(",", resolved.Distinct().Select(r => $"\"{r}\""));
            entry.SectionRefs = $"[{items}]";
        }

        return entries;
    }
}
