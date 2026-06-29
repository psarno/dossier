using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DossierApi.Models;

namespace DossierApi.Services;

public class SourceCatalogService
{
    private static readonly Regex SocialRegex = new(@"@\w[\w.]*", RegexOptions.Compiled);
    private static readonly Regex SourceLabelRegex = new(@"\bSources?\s*:\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DateRegex = new(@"\b(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t\.?|tember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?\s+\d{1,2},\s+\d{4}\b|\b(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t\.?|tember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?\s+\d{4}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] OutletNames =
    [
        "Reuters", "CNN", "New York Times", "NYT", "NPR", "BBC", "AP", "Associated Press",
        "Politico", "Bloomberg", "Guardian", "Business Insider", "Washington Post",
        "CBS News", "NBC", "PBS", "LA Magazine", "Middle East Eye", "Al Jazeera",
        "Santa Fe New Mexican", "ABQ Journal", "Times of Israel", "Haaretz", "CNBC",
        "Wikipedia", "Just Security", "Dropsite News", "Jacobin", "Rev.com"
    ];
    private static readonly string[] GovernmentKeywords =
    [
        "DOJ", "FBI", "House Oversight", "Senate", "Committee", "Cabinet Office",
        "House of Commons", "Metropolitan Police", "NYS DFS", "Department of Justice",
        "Joint Statement", "Congressional", "Parliamentary", "government documents"
    ];
    private static readonly string[] LegalKeywords =
    [
        "court filing", "court filings", "deposition", "depositions", "transcript",
        "transcripts", "consent order", "Rule 56.1", "civil suit", "sworn testimony"
    ];

    public GeneratedSourcesReport Generate(string? summaryContent, string summaryVersion, string? namesContent, string namesVersion)
    {
        var fragments = new List<ExtractedFragment>();
        if (!string.IsNullOrWhiteSpace(summaryContent))
            fragments.AddRange(ExtractFragments(summaryContent!, "summary"));
        if (!string.IsNullOrWhiteSpace(namesContent))
            fragments.AddRange(ExtractFragments(namesContent!, "names"));

        var report = BuildReport(fragments, summaryVersion, namesVersion);
        return report;
    }

    public void WriteArtifacts(string outputDir, GeneratedSourcesReport report, string filePrefix = "sources.generated")
    {
        Directory.CreateDirectory(outputDir);
        var jsonPath = Path.Combine(outputDir, $"{filePrefix}.json");
        var markdownPath = Path.Combine(outputDir, $"{filePrefix}.md");

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        File.WriteAllText(jsonPath, json);
        File.WriteAllText(markdownPath, RenderMarkdown(report));
    }

    private static List<ExtractedFragment> ExtractFragments(string markdown, string document)
    {
        var fragments = new List<ExtractedFragment>();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var contextLabel = "";

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (trimmed.StartsWith("## "))
                contextLabel = trimmed[3..].Trim();
            else if (trimmed.StartsWith("### "))
                contextLabel = trimmed[4..].Trim();

            var sourceMatch = SourceLabelRegex.Match(line);
            if (sourceMatch.Success)
            {
                var sourceText = line[(sourceMatch.Index + sourceMatch.Length)..].Trim().Trim('*');
                foreach (var fragment in SplitTopLevelFragments(sourceText))
                    AddFragment(fragments, fragment, document, i + 1, contextLabel, "source_line");
                continue;
            }

            if (LooksLikeInlineSourceCitation(line))
            {
                AddFragment(fragments, trimmed.Trim('*'), document, i + 1, contextLabel, "inline_source");
            }
        }

        return fragments;
    }

    private static void AddFragment(List<ExtractedFragment> fragments, string raw, string document, int lineNumber, string contextLabel, string kind)
    {
        raw = raw.Trim().Trim('*').Trim();
        if (string.IsNullOrWhiteSpace(raw)) return;
        fragments.Add(new ExtractedFragment
        {
            Raw = raw,
            Document = document,
            LineNumber = lineNumber,
            ContextLabel = contextLabel,
            Kind = kind
        });
    }

    private static IEnumerable<string> SplitTopLevelFragments(string sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
            yield break;

        var parts = sourceText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
            yield return part.Trim().TrimEnd('.');
    }

    private static bool LooksLikeInlineSourceCitation(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        if (line.Contains("Source:", StringComparison.OrdinalIgnoreCase) || line.Contains("Sources:", StringComparison.OrdinalIgnoreCase))
            return true;

        int score = 0;
        if (OutletNames.Any(o => line.Contains(o, StringComparison.OrdinalIgnoreCase))) score++;
        if (GovernmentKeywords.Any(k => line.Contains(k, StringComparison.OrdinalIgnoreCase))) score++;
        if (LegalKeywords.Any(k => line.Contains(k, StringComparison.OrdinalIgnoreCase))) score++;
        if (SocialRegex.IsMatch(line)) score++;
        if (DateRegex.IsMatch(line)) score++;

        return score >= 2;
    }

    private static GeneratedSourcesReport BuildReport(List<ExtractedFragment> fragments, string summaryVersion, string namesVersion)
    {
        var byKey = new Dictionary<string, GeneratedSourceItem>(StringComparer.OrdinalIgnoreCase);
        var review = new List<SourceReviewItem>();

        foreach (var fragment in fragments)
        {
            var parsed = ParseFragment(fragment.Raw);

            foreach (var item in ExpandItems(parsed, fragment, byKey))
            {
                item.Occurrences.Add(new SourceOccurrence
                {
                    Document = fragment.Document,
                    LineNumber = fragment.LineNumber,
                    ContextLabel = fragment.ContextLabel
                });
            }

            if (parsed.NeedsReview)
            {
                review.Add(new SourceReviewItem
                {
                    Raw = fragment.Raw,
                    Document = fragment.Document,
                    LineNumber = fragment.LineNumber,
                    ContextLabel = fragment.ContextLabel,
                    Reason = parsed.ReviewReason
                });
            }
        }

        var items = byKey.Values
            .OrderBy(i => TypeOrder(i.Type))
            .ThenBy(i => i.NormalizedLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new GeneratedSourcesReport
        {
            GeneratedAt = DateTime.UtcNow.ToString("O"),
            SummaryVersion = summaryVersion,
            NamesVersion = namesVersion,
            Totals = new SourceTotals
            {
                TotalFragments = fragments.Count,
                ParsedItems = items.Count,
                AmbiguousItems = review.Count,
            },
            Items = items,
            Review = review
        };
    }

    private static IEnumerable<GeneratedSourceItem> ExpandItems(ParsedSourceFragment parsed, ExtractedFragment fragment, Dictionary<string, GeneratedSourceItem> byKey)
    {
        yield return GetOrAddItem(byKey, parsed, fragment);
    }

    private static GeneratedSourceItem GetOrAddItem(Dictionary<string, GeneratedSourceItem> byKey, ParsedSourceFragment parsed, ExtractedFragment fragment)
    {
        var key = $"{parsed.Type}|{parsed.NormalizedLabel}".ToLowerInvariant();
        if (!byKey.TryGetValue(key, out var item))
        {
            item = new GeneratedSourceItem
            {
                Raw = fragment.Raw,
                RawVariants = [fragment.Raw],
                NormalizedLabel = parsed.NormalizedLabel,
                Type = parsed.Type,
                Author = parsed.Author,
                Outlet = parsed.Outlet,
                Account = parsed.Account,
                Platform = parsed.Platform,
                DateText = parsed.DateText,
                Notes = parsed.Notes,
                Confidence = parsed.Confidence,
                NeedsReview = parsed.NeedsReview
            };
            byKey[key] = item;
        }
        else
        {
            if (!item.RawVariants.Contains(fragment.Raw, StringComparer.Ordinal))
                item.RawVariants.Add(fragment.Raw);
            item.SourceDocuments = [.. item.SourceDocuments.Union([fragment.Document], StringComparer.OrdinalIgnoreCase).OrderBy(x => x)];
            item.NeedsReview |= parsed.NeedsReview;
            item.Confidence = MaxConfidence(item.Confidence, parsed.Confidence);
        }

        if (!item.SourceDocuments.Contains(fragment.Document, StringComparer.OrdinalIgnoreCase))
            item.SourceDocuments.Add(fragment.Document);

        return item;
    }

    private static ParsedSourceFragment ParseFragment(string raw)
    {
        var normalized = raw.Trim().Trim('*');
        var dateText = DateRegex.Match(normalized) is { Success: true } dateMatch ? dateMatch.Value : null;

        var socialMatch = SocialRegex.Match(normalized);
        if (socialMatch.Success)
        {
            var platform = normalized.Contains("X/Twitter", StringComparison.OrdinalIgnoreCase) ? "X/Twitter"
                : normalized.Contains("Twitter", StringComparison.OrdinalIgnoreCase) ? "Twitter"
                : normalized.Contains("X", StringComparison.OrdinalIgnoreCase) ? "X"
                : null;

            return new ParsedSourceFragment
            {
                NormalizedLabel = socialMatch.Value,
                Type = "social",
                Account = socialMatch.Value,
                Platform = platform,
                DateText = dateText,
                Notes = normalized,
                Confidence = "high",
                NeedsReview = false
            };
        }

        var outlet = OutletNames.FirstOrDefault(o => normalized.Contains(o, StringComparison.OrdinalIgnoreCase));
        if (outlet != null)
        {
            var author = normalized.Contains(',', StringComparison.Ordinal) ? normalized.Split(',', 2)[0].Trim() : null;
            return new ParsedSourceFragment
            {
                NormalizedLabel = BuildNormalizedLabel(author, outlet, dateText) ?? outlet,
                Type = "journalism",
                Author = author,
                Outlet = outlet,
                DateText = dateText,
                Notes = normalized,
                Confidence = dateText != null ? "high" : "medium",
                NeedsReview = dateText == null,
                ReviewReason = dateText == null ? "Journalism source missing recognizable date." : ""
            };
        }

        if (LegalKeywords.Any(k => normalized.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            return new ParsedSourceFragment
            {
                NormalizedLabel = normalized,
                Type = "legal",
                DateText = dateText,
                Notes = normalized,
                Confidence = dateText != null ? "medium" : "low",
                NeedsReview = true,
                ReviewReason = "Legal source captured as raw text; normalization incomplete."
            };
        }

        if (GovernmentKeywords.Any(k => normalized.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            return new ParsedSourceFragment
            {
                NormalizedLabel = normalized,
                Type = "government",
                DateText = dateText,
                Notes = normalized,
                Confidence = dateText != null ? "medium" : "low",
                NeedsReview = true,
                ReviewReason = "Government source captured as raw text; normalization incomplete."
            };
        }

        return new ParsedSourceFragment
        {
            NormalizedLabel = normalized,
            Type = "unknown",
            DateText = dateText,
            Notes = normalized,
            Confidence = "low",
            NeedsReview = true,
            ReviewReason = "Source fragment could not be confidently normalized."
        };
    }

    private static string? BuildNormalizedLabel(string? author, string outlet, string? dateText)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(author)) parts.Add(author!);
        parts.Add(outlet);
        if (!string.IsNullOrWhiteSpace(dateText)) parts.Add(dateText!);
        return parts.Count == 0 ? null : string.Join(" — ", parts);
    }

    private static string MaxConfidence(string left, string right)
    {
        return (ConfidenceRank(left), ConfidenceRank(right)) switch
        {
            (var l, var r) when l >= r => left,
            _ => right
        };
    }

    private static int ConfidenceRank(string confidence) => confidence switch
    {
        "high" => 3,
        "medium" => 2,
        _ => 1
    };

    private static int TypeOrder(string type) => type switch
    {
        "government" => 0,
        "legal" => 1,
        "journalism" => 2,
        "social" => 3,
        "general" => 4,
        _ => 5
    };

    private static string RenderMarkdown(GeneratedSourcesReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Generated Sources");
        sb.AppendLine($"*Generated {report.GeneratedAt} from summary {report.SummaryVersion} and names {report.NamesVersion}.*");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine($"- Parsed items: {report.Totals.ParsedItems}");
        sb.AppendLine($"- Total fragments: {report.Totals.TotalFragments}");
        sb.AppendLine($"- Needs review: {report.Review.Count}");
        sb.AppendLine();

        foreach (var group in report.Items.GroupBy(i => i.Type).OrderBy(g => TypeOrder(g.Key)))
        {
            sb.AppendLine($"## {DisplayType(group.Key)}");
            sb.AppendLine();
            foreach (var item in group)
            {
                sb.AppendLine($"### {item.NormalizedLabel}");
                sb.AppendLine($"- Type: `{item.Type}`");
                sb.AppendLine($"- Confidence: `{item.Confidence}`");
                sb.AppendLine($"- Documents: {string.Join(", ", item.SourceDocuments.OrderBy(x => x))}");
                if (!string.IsNullOrWhiteSpace(item.DateText))
                    sb.AppendLine($"- Date: {item.DateText}");
                sb.AppendLine($"- Raw: {item.Raw}");
                if (item.RawVariants.Count > 1)
                    sb.AppendLine($"- Variants: {string.Join(" | ", item.RawVariants)}");
                sb.AppendLine($"- Occurrences: {string.Join("; ", item.Occurrences.Select(o => $"{o.Document}:{o.LineNumber} ({o.ContextLabel})"))}");
                sb.AppendLine();
            }
        }

        if (report.Review.Count > 0)
        {
            sb.AppendLine("## Needs Review");
            sb.AppendLine();
            foreach (var item in report.Review)
                sb.AppendLine($"- `{item.Document}:{item.LineNumber}` {item.Raw} [{item.Reason}]");
        }

        return sb.ToString();
    }

    private static string DisplayType(string type) => type switch
    {
        "government" => "Government",
        "legal" => "Legal",
        "journalism" => "Journalism",
        "social" => "Social",
        "general" => "General",
        _ => "Unknown"
    };

    private sealed class ExtractedFragment
    {
        public string Raw { get; set; } = "";
        public string Document { get; set; } = "";
        public int LineNumber { get; set; }
        public string ContextLabel { get; set; } = "";
        public string Kind { get; set; } = "";
    }

    private sealed class ParsedSourceFragment
    {
        public string NormalizedLabel { get; set; } = "";
        public string Type { get; set; } = "";
        public string? Author { get; set; }
        public string? Outlet { get; set; }
        public string? Account { get; set; }
        public string? Platform { get; set; }
        public string? DateText { get; set; }
        public string? Notes { get; set; }
        public string Confidence { get; set; } = "low";
        public bool NeedsReview { get; set; }
        public string ReviewReason { get; set; } = "";
    }
}
