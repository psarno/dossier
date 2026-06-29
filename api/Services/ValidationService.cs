using Microsoft.Data.Sqlite;

namespace DossierApi.Services;

public class ValidationService
{
    private readonly IConfiguration _config;
    private readonly ILogger<ValidationService> _logger;

    public ValidationService(IConfiguration config, ILogger<ValidationService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public (bool Valid, List<string> Errors) Validate(SqliteConnection conn)
    {
        var errors = new List<string>();
        int minSections = _config.GetValue("PIPELINE_MIN_SECTIONS", 30);
        int minEntries = _config.GetValue("PIPELINE_MIN_ENTRIES", 50);

        // 1. Section count
        var sectionCount = ScalarInt(conn, "SELECT COUNT(*) FROM sections");
        if (sectionCount < minSections)
            errors.Add($"Section count {sectionCount} is below minimum {minSections}");

        // 2. No empty bodies
        var emptySections = ScalarInt(conn, "SELECT COUNT(*) FROM sections WHERE TRIM(body) = ''");
        if (emptySections > 0)
            errors.Add($"{emptySections} section(s) have empty body");

        // 3. Entry count
        var entryCount = ScalarInt(conn, "SELECT COUNT(*) FROM entries");
        if (entryCount < minEntries)
            errors.Add($"Entry count {entryCount} is below minimum {minEntries}");

        // 4. FTS tables non-empty
        var ftsSections = ScalarInt(conn, "SELECT COUNT(*) FROM sections_fts");
        if (ftsSections == 0)
            errors.Add("sections_fts is empty");

        var ftsEntries = ScalarInt(conn, "SELECT COUNT(*) FROM entries_fts");
        if (ftsEntries == 0)
            errors.Add("entries_fts is empty");

        // 5. Required slugs
        string[] requiredSlugs = ["what-this-document-is", "the-origin-how-he-got-there", "what-he-actually-built-the-model"];
        foreach (var slug in requiredSlugs)
        {
            var exists = ScalarInt(conn, $"SELECT COUNT(*) FROM sections WHERE slug = '{slug}'");
            if (exists == 0)
                _logger.LogWarning("Expected slug not found: {Slug}", slug); // warning, not hard error
        }

        // 6. Metadata present
        var metaCount = ScalarInt(conn, "SELECT COUNT(*) FROM metadata");
        if (metaCount == 0)
            errors.Add("metadata table is empty");

        if (errors.Count == 0)
            _logger.LogInformation("Validation passed: {Sections} sections, {Entries} entries", sectionCount, entryCount);
        else
            _logger.LogWarning("Validation failed: {Errors}", string.Join("; ", errors));

        return (errors.Count == 0, errors);
    }

    private static int ScalarInt(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }
}
