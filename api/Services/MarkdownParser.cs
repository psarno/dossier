using System.Text.RegularExpressions;
using DossierApi.Models;

namespace DossierApi.Services;

public static class MarkdownParser
{
    private static readonly string[] _fallbackTags =
    [
        "CONFIRMED", "CORROBORATED", "DOCUMENTED CLAIM", "CONFIRMED GOVT ACTION", "ANOMALOUS"
    ];

    // Extracts version string from filename, e.g. "subject_summary_v44.md" → "v44"
    public static string ExtractVersion(string filename)
    {
        var m = Regex.Match(Path.GetFileName(filename), @"_v(\d+)", RegexOptions.IgnoreCase);
        return m.Success ? $"v{m.Groups[1].Value}" : "unknown";
    }

    // Split summary document on ## headings
    public static List<Section> ParseSummary(string markdown, string version, IEnumerable<string>? tagKeys = null)
    {
        var tags = tagKeys?.ToArray() ?? _fallbackTags;
        var lines = markdown.Split('\n');
        var sections = new List<Section>();
        var currentTitle = "";
        var currentLines = new List<string>();
        int sortOrder = 0;

        void Flush()
        {
            if (string.IsNullOrWhiteSpace(currentTitle)) return;
            var body = string.Join('\n', currentLines).Trim();
            if (string.IsNullOrWhiteSpace(body)) return;
            sections.Add(new Section
            {
                Slug = MakeSlug(currentTitle),
                Title = CleanTitle(currentTitle),
                Body = body,
                DocType = "summary",
                DocVersion = version,
                SortOrder = sortOrder++,
                TagsPresent = ExtractTagsJson(body, tags),
                SourceHeading = currentTitle
            });
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("## "))
            {
                Flush();
                currentTitle = line[3..].Trim();
                currentLines.Clear();
            }
            else
            {
                currentLines.Add(line);
            }
        }
        Flush();

        return sections;
    }

    public static Section? ParseAnalyticalFramework(string markdown, string version)
    {
        var normalized = markdown.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');

        string title = "Analytical Framework";
        var bodyStartIndex = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith("# ")) continue;

            title = line[2..].Trim();
            bodyStartIndex = i + 1;
            break;
        }

        var body = string.Join('\n', lines.Skip(bodyStartIndex)).Trim();
        if (string.IsNullOrWhiteSpace(body))
            return null;

        return new Section
        {
            Slug = "analytical-framework",
            Title = title,
            Body = body,
            DocType = "analytical_framework",
            DocVersion = version,
            SortOrder = 0,
            TagsPresent = "[]",
            SourceHeading = title
        };
    }

    // Split names index on ### headings, detect Tier boundary
    public static List<Entry> ParseNamesIndex(string markdown, string version)
    {
        var lines = markdown.Split('\n');
        var entries = new List<Entry>();
        var currentName = "";
        var currentLines = new List<string>();
        int currentTier = 1;

        bool inTier2 = false;
        bool seenTier1Section = false;

        void Flush()
        {
            if (string.IsNullOrWhiteSpace(currentName)) return;
            var body = string.Join('\n', currentLines).Trim();
            entries.Add(new Entry
            {
                Slug = MakeSlug(currentName),
                Name = currentName.Trim(),
                Tier = currentTier,
                Description = body,
                SectionRefs = ExtractSectionRefsJson(body),
                DocVersion = version
            });
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("### "))
            {
                Flush();
                currentName = line[4..].Trim();
                currentLines.Clear();

                // Check if this is actually a tier boundary marker or a removed entry
                if (currentName.Contains("Research Queue", StringComparison.OrdinalIgnoreCase) ||
                    currentName.Contains("Tier 2", StringComparison.OrdinalIgnoreCase))
                {
                    inTier2 = true;
                    currentName = ""; // don't create an entry for the boundary heading
                }
                else if (currentName.StartsWith("~~") || currentName.Contains("REMOVED", StringComparison.OrdinalIgnoreCase))
                {
                    currentName = ""; // skip removed entries
                }
                else
                {
                    currentTier = inTier2 ? 2 : 1;
                    currentName = StripEntryPrefix(currentName);
                }
            }
            else if (line.StartsWith("## "))
            {
                // Top-level headings define tier boundaries
                Flush();
                currentName = "";
                currentLines.Clear();
                if (line.Contains("TIER 1", StringComparison.OrdinalIgnoreCase))
                {
                    seenTier1Section = true;
                    inTier2 = false;
                }
                else if (seenTier1Section)
                {
                    // Any top-level section after TIER 1 is Tier 2 content
                    inTier2 = true;
                }
            }
            else
            {
                currentLines.Add(line);
            }
        }
        Flush();

        return entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .Where(e => !e.Name.Contains("partially redacted", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // Generate URL-safe slug from title
    public static string MakeSlug(string title)
    {
        // Remove markdown formatting, lowercase, replace spaces/punctuation with hyphens
        var s = Regex.Replace(title, @"[*_`#\[\]]", "");
        s = s.ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9\s-]", "");
        s = Regex.Replace(s, @"\s+", "-");
        s = Regex.Replace(s, @"-+", "-");
        s = s.Trim('-');
        if (s.Length > 80) s = s[..80].TrimEnd('-');
        return s;
    }

    // Remove leading Roman numerals + "." from titles like "II. The Origin"
    private static string CleanTitle(string title)
    {
        return Regex.Replace(title, @"^[IVXLCDM]+\.\s*", "").Trim();
    }

    // Strip document reference prefixes from entry names:
    // "1. Donald Trump" → "Donald Trump"
    // "DR1. Timothy Cook Draper" → "Timothy Cook Draper"
    // "E12. Reid Hoffman" → "Reid Hoffman"
    // "KYC1. Thomas Purdy" → "Thomas Purdy"
    // "9a. Ariane de Rothschild" → "Ariane de Rothschild"
    private static string StripEntryPrefix(string name)
    {
        // Matches single: "1.", "E12.", "DR1.", "9a."
        // Also matches ranges: "E27–E29.", "E3–E4.", "E9–E11."
        return Regex.Replace(name.Trim(), @"^[A-Za-z]{0,5}\d+[a-z]*(?:[–\-][A-Za-z]{0,5}\d+[a-z]*)?\.\s*", "").Trim();
    }

    // Return JSON array of tag names found in body text
    public static string ExtractTagsJson(string body, IEnumerable<string>? tagKeys = null)
    {
        var tags = tagKeys ?? _fallbackTags;
        var found = new HashSet<string>();
        foreach (var tag in tags)
        {
            if (body.Contains($"[{tag}]", StringComparison.OrdinalIgnoreCase))
                found.Add(tag);
        }
        var items = string.Join(",", found.Select(t => $"\"{t}\""));
        return $"[{items}]";
    }

    // Extract section slug references from "**Summary sections:** Title1; Title2" field.
    // Slugs stored here are unresolved; PipelineService resolves them to actual section slugs
    // after AI enrichment finalises the section slug list.
    private static string ExtractSectionRefsJson(string body)
    {
        var match = Regex.Match(body, @"\*\*Summary sections:\*\*\s*([^\n]+)", RegexOptions.IgnoreCase);
        if (!match.Success) return "[]";

        var refText = match.Groups[1].Value.Trim();

        // Primary separator is ; — fall back to , when no semicolons present
        var rawParts = refText.Contains(';')
            ? refText.Split(';')
            : refText.Split(',');

        var refs = new List<string>();
        foreach (var part in rawParts)
        {
            // Strip trailing parenthetical: "Russian Connection (detail notes)" → "Russian Connection"
            var clean = Regex.Replace(part.Trim(), @"\s*\(.*\)\s*$", "").Trim().TrimEnd('.', ',', ';', ':');
            if (!string.IsNullOrWhiteSpace(clean) && clean.Length > 2)
                refs.Add(MakeSlug(clean));
        }

        var items = string.Join(",", refs.Distinct().Select(r => $"\"{r}\""));
        return $"[{items}]";
    }
}
