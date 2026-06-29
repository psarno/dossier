using Microsoft.Data.Sqlite;
using DossierApi.Models;

namespace DossierApi.Services;

public class DatabaseService
{
    private readonly string _dbPath;
    private readonly ILogger<DatabaseService> _logger;

    public DatabaseService(IConfiguration config, ILogger<DatabaseService> logger)
    {
        _dbPath = config["DB_PATH"] ?? "/data/dossier.db";
        _logger = logger;

        // Ensure schema exists so read endpoints return [] instead of 500 before pipeline runs
        using var conn = OpenConnection();
        InitSchema(conn);
    }

    public SqliteConnection OpenConnection(string? path = null, bool useWal = true)
    {
        var conn = new SqliteConnection($"Data Source={path ?? _dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = useWal
            ? "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;"
            : "PRAGMA journal_mode=DELETE; PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();
        return conn;
    }

    public void InitSchema(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS metadata (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS sections (
                id           INTEGER PRIMARY KEY,
                slug         TEXT    UNIQUE NOT NULL,
                title        TEXT    NOT NULL,
                body         TEXT    NOT NULL,
                doc_type     TEXT    NOT NULL,
                doc_version  TEXT    NOT NULL,
                sort_order   INTEGER NOT NULL,
                tags_present TEXT    NOT NULL DEFAULT '[]'
            );

            CREATE VIRTUAL TABLE IF NOT EXISTS sections_fts USING fts5(
                title, body,
                content='sections', content_rowid='id'
            );

            CREATE TABLE IF NOT EXISTS entries (
                id           INTEGER PRIMARY KEY,
                slug         TEXT    UNIQUE NOT NULL,
                name         TEXT    NOT NULL,
                tier         INTEGER NOT NULL,
                description  TEXT    NOT NULL,
                section_refs TEXT    NOT NULL DEFAULT '[]',
                doc_version  TEXT    NOT NULL
            );

            CREATE VIRTUAL TABLE IF NOT EXISTS entries_fts USING fts5(
                name, description,
                content='entries', content_rowid='id'
            );

            CREATE TABLE IF NOT EXISTS ai_section_cache (
                section_hash   TEXT NOT NULL,
                provider_name  TEXT NOT NULL,
                model_name     TEXT NOT NULL,
                prompt_version TEXT NOT NULL,
                slug           TEXT NOT NULL,
                title          TEXT NOT NULL,
                tags_present   TEXT NOT NULL DEFAULT '[]',
                cached_at      TEXT NOT NULL,
                PRIMARY KEY (section_hash, provider_name, model_name, prompt_version)
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public void PopulateFts(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sections_fts(sections_fts) VALUES('rebuild');
            INSERT INTO entries_fts(entries_fts) VALUES('rebuild');
            """;
        cmd.ExecuteNonQuery();
    }

    // ── Read endpoints ────────────────────────────────────────────────────────

    public List<SectionSummary> GetAllSections()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, slug, title, doc_type, doc_version, sort_order, tags_present FROM sections ORDER BY doc_type, sort_order";
        using var reader = cmd.ExecuteReader();
        var results = new List<SectionSummary>();
        while (reader.Read())
            results.Add(ReadSectionSummary(reader));
        return results;
    }

    public Section? GetSection(string slug)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, slug, title, body, doc_type, doc_version, sort_order, tags_present FROM sections WHERE slug = @slug";
        cmd.Parameters.AddWithValue("@slug", slug);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new Section
        {
            Id = reader.GetInt32(0),
            Slug = reader.GetString(1),
            Title = reader.GetString(2),
            Body = reader.GetString(3),
            DocType = reader.GetString(4),
            DocVersion = reader.GetString(5),
            SortOrder = reader.GetInt32(6),
            TagsPresent = reader.GetString(7)
        };
    }

    public List<Section> GetSectionsByDocType(string docType)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, slug, title, body, doc_type, doc_version, sort_order, tags_present
            FROM sections
            WHERE doc_type = @docType
            ORDER BY sort_order, title COLLATE NOCASE
            """;
        cmd.Parameters.AddWithValue("@docType", docType);
        using var reader = cmd.ExecuteReader();
        var results = new List<Section>();
        while (reader.Read())
        {
            results.Add(new Section
            {
                Id = reader.GetInt32(0),
                Slug = reader.GetString(1),
                Title = reader.GetString(2),
                Body = reader.GetString(3),
                DocType = reader.GetString(4),
                DocVersion = reader.GetString(5),
                SortOrder = reader.GetInt32(6),
                TagsPresent = reader.GetString(7)
            });
        }
        return results;
    }

    public List<EntrySummary> GetAllEntries()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, slug, name, tier, section_refs FROM entries ORDER BY tier, name COLLATE NOCASE";
        using var reader = cmd.ExecuteReader();
        var results = new List<EntrySummary>();
        while (reader.Read())
            results.Add(new EntrySummary
            {
                Id = reader.GetInt32(0),
                Slug = reader.GetString(1),
                Name = reader.GetString(2),
                Tier = reader.GetInt32(3),
                SectionRefs = reader.GetString(4)
            });
        return results;
    }

    public Entry? GetEntry(string slug)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, slug, name, tier, description, section_refs, doc_version FROM entries WHERE slug = @slug";
        cmd.Parameters.AddWithValue("@slug", slug);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new Entry
        {
            Id = reader.GetInt32(0),
            Slug = reader.GetString(1),
            Name = reader.GetString(2),
            Tier = reader.GetInt32(3),
            Description = reader.GetString(4),
            SectionRefs = reader.GetString(5),
            DocVersion = reader.GetString(6)
        };
    }

    public List<Entry> GetEntriesDetailed()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, slug, name, tier, description, section_refs, doc_version
            FROM entries
            ORDER BY tier, name COLLATE NOCASE
            """;
        using var reader = cmd.ExecuteReader();
        var results = new List<Entry>();
        while (reader.Read())
        {
            results.Add(new Entry
            {
                Id = reader.GetInt32(0),
                Slug = reader.GetString(1),
                Name = reader.GetString(2),
                Tier = reader.GetInt32(3),
                Description = reader.GetString(4),
                SectionRefs = reader.GetString(5),
                DocVersion = reader.GetString(6)
            });
        }
        return results;
    }

    public List<SearchResult> Search(string query, string type)
    {
        using var conn = OpenConnection();
        var results = new List<SearchResult>();

        if (type is "all" or "sections")
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT s.slug, s.title, snippet(sections_fts, 1, '<mark>', '</mark>', '…', 32), rank
                FROM sections_fts
                JOIN sections s ON s.id = sections_fts.rowid
                WHERE sections_fts MATCH @q
                  AND s.doc_type = 'summary'
                ORDER BY rank
                LIMIT 20
                """;
            cmd.Parameters.AddWithValue("@q", query);
            try
            {
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    results.Add(new SearchResult
                    {
                        ResultType = "section",
                        Slug = reader.GetString(0),
                        Title = reader.GetString(1),
                        Snippet = reader.GetString(2),
                        Rank = reader.GetDouble(3)
                    });
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Section FTS search failed: {Message}", ex.Message);
            }
        }

        if (type is "all" or "entries")
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT e.slug, e.name, snippet(entries_fts, 1, '<mark>', '</mark>', '…', 32), rank
                FROM entries_fts
                JOIN entries e ON e.id = entries_fts.rowid
                WHERE entries_fts MATCH @q
                ORDER BY rank
                LIMIT 20
                """;
            cmd.Parameters.AddWithValue("@q", query);
            try
            {
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    results.Add(new SearchResult
                    {
                        ResultType = "entry",
                        Slug = reader.GetString(0),
                        Title = reader.GetString(1),
                        Snippet = reader.GetString(2),
                        Rank = reader.GetDouble(3)
                    });
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Entry FTS search failed: {Message}", ex.Message);
            }
        }

        return results.OrderBy(r => r.Rank).ToList();
    }

    public Dictionary<string, string> GetMetadata()
    {
        if (!File.Exists(_dbPath)) return new Dictionary<string, string>();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM metadata";
        var dict = new Dictionary<string, string>();
        try
        {
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                dict[reader.GetString(0)] = reader.GetString(1);
        }
        catch { /* table may not exist yet */ }
        return dict;
    }

    public List<AiSectionCacheEntry> GetAiSectionCacheEntries()
    {
        if (!File.Exists(_dbPath)) return [];
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT section_hash, provider_name, model_name, prompt_version, slug, title, tags_present, cached_at
            FROM ai_section_cache
            """;

        var results = new List<AiSectionCacheEntry>();
        try
        {
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new AiSectionCacheEntry
                {
                    SectionHash = reader.GetString(0),
                    ProviderName = reader.GetString(1),
                    ModelName = reader.GetString(2),
                    PromptVersion = reader.GetString(3),
                    Slug = reader.GetString(4),
                    Title = reader.GetString(5),
                    TagsPresent = reader.GetString(6),
                    CachedAt = reader.GetString(7)
                });
            }
        }
        catch { /* table may not exist yet */ }

        return results;
    }

    // ── Write helpers (used by pipeline) ─────────────────────────────────────

    public void InsertSection(SqliteConnection conn, Section s)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO sections (slug, title, body, doc_type, doc_version, sort_order, tags_present)
            VALUES (@slug, @title, @body, @doc_type, @doc_version, @sort_order, @tags_present)
            """;
        cmd.Parameters.AddWithValue("@slug", s.Slug);
        cmd.Parameters.AddWithValue("@title", s.Title);
        cmd.Parameters.AddWithValue("@body", s.Body);
        cmd.Parameters.AddWithValue("@doc_type", s.DocType);
        cmd.Parameters.AddWithValue("@doc_version", s.DocVersion);
        cmd.Parameters.AddWithValue("@sort_order", s.SortOrder);
        cmd.Parameters.AddWithValue("@tags_present", s.TagsPresent);
        cmd.ExecuteNonQuery();
    }

    public void InsertEntry(SqliteConnection conn, Entry e)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO entries (slug, name, tier, description, section_refs, doc_version)
            VALUES (@slug, @name, @tier, @description, @section_refs, @doc_version)
            """;
        cmd.Parameters.AddWithValue("@slug", e.Slug);
        cmd.Parameters.AddWithValue("@name", e.Name);
        cmd.Parameters.AddWithValue("@tier", e.Tier);
        cmd.Parameters.AddWithValue("@description", e.Description);
        cmd.Parameters.AddWithValue("@section_refs", e.SectionRefs);
        cmd.Parameters.AddWithValue("@doc_version", e.DocVersion);
        cmd.ExecuteNonQuery();
    }

    public void SetMetadata(SqliteConnection conn, string key, string value)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO metadata (key, value) VALUES (@key, @value)";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.ExecuteNonQuery();
    }

    public void InsertAiSectionCacheEntry(SqliteConnection conn, AiSectionCacheEntry entry)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO ai_section_cache (
                section_hash, provider_name, model_name, prompt_version, slug, title, tags_present, cached_at
            )
            VALUES (
                @section_hash, @provider_name, @model_name, @prompt_version, @slug, @title, @tags_present, @cached_at
            )
            """;
        cmd.Parameters.AddWithValue("@section_hash", entry.SectionHash);
        cmd.Parameters.AddWithValue("@provider_name", entry.ProviderName);
        cmd.Parameters.AddWithValue("@model_name", entry.ModelName);
        cmd.Parameters.AddWithValue("@prompt_version", entry.PromptVersion);
        cmd.Parameters.AddWithValue("@slug", entry.Slug);
        cmd.Parameters.AddWithValue("@title", entry.Title);
        cmd.Parameters.AddWithValue("@tags_present", entry.TagsPresent);
        cmd.Parameters.AddWithValue("@cached_at", entry.CachedAt);
        cmd.ExecuteNonQuery();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SectionSummary ReadSectionSummary(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(0),
        Slug = r.GetString(1),
        Title = r.GetString(2),
        DocType = r.GetString(3),
        DocVersion = r.GetString(4),
        SortOrder = r.GetInt32(5),
        TagsPresent = r.GetString(6)
    };
}
