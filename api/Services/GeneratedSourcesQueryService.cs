using System.Text.Json;
using DossierApi.Models;

namespace DossierApi.Services;

public class GeneratedSourcesQueryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] GroupOrder =
    [
        "government",
        "legal",
        "journalism",
        "social",
        "general",
        "unknown"
    ];

    private readonly IConfiguration _config;
    private readonly object _gate = new();
    private GeneratedSourcesReport? _cachedReport;
    private DateTime _cachedWriteTimeUtc;
    private string? _cachedPath;

    public GeneratedSourcesQueryService(IConfiguration config)
    {
        _config = config;
    }

    public GeneratedSourcesSummaryResponse? GetSummary()
    {
        var report = LoadReport();
        return report is null ? null : new GeneratedSourcesSummaryResponse
        {
            GeneratedAt = report.GeneratedAt,
            SummaryVersion = report.SummaryVersion,
            NamesVersion = report.NamesVersion,
            Totals = report.Totals,
            Groups = BuildGroups(report.Items)
                .Select(group => new GeneratedSourcesGroupSummary
                {
                    Key = group.Key,
                    Label = group.Label,
                    ItemCount = group.Items.Count
                })
                .ToList()
        };
    }

    public GeneratedSourcesGroupResponse? GetGroup(string key)
    {
        var report = LoadReport();
        if (report is null)
            return null;

        var group = BuildGroups(report.Items)
            .FirstOrDefault(candidate => string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase));

        return group is null
            ? null
            : new GeneratedSourcesGroupResponse
            {
                Key = group.Key,
                Label = group.Label,
                ItemCount = group.Items.Count,
                Items = group.Items
            };
    }

    public List<GeneratedSourcesGroupResponse>? Search(string query)
    {
        var report = LoadReport();
        if (report is null)
            return null;

        var normalizedQuery = NormalizeForComparison(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
            return [];

        return BuildGroups(report.Items.Where(item => MatchesSearch(item, normalizedQuery)))
            .Select(group => new GeneratedSourcesGroupResponse
            {
                Key = group.Key,
                Label = group.Label,
                ItemCount = group.Items.Count,
                Items = group.Items
            })
            .ToList();
    }

    private GeneratedSourcesReport? LoadReport()
    {
        var jsonPath = ResolveJsonPath();
        if (!File.Exists(jsonPath))
            return null;

        var writeTimeUtc = File.GetLastWriteTimeUtc(jsonPath);

        lock (_gate)
        {
            if (_cachedReport is not null &&
                string.Equals(_cachedPath, jsonPath, StringComparison.Ordinal) &&
                _cachedWriteTimeUtc == writeTimeUtc)
            {
                return _cachedReport;
            }

            var json = File.ReadAllText(jsonPath);
            var report = JsonSerializer.Deserialize<GeneratedSourcesReport>(json, JsonOptions);
            if (report is null)
                return null;

            _cachedReport = report;
            _cachedPath = jsonPath;
            _cachedWriteTimeUtc = writeTimeUtc;
            return report;
        }
    }

    private string ResolveJsonPath()
    {
        var dbPath = _config["DB_PATH"] ?? "/data/dossier.db";
        return Path.Combine(Path.GetDirectoryName(dbPath)!, "sources.generated.json");
    }

    private static List<GroupBucket> BuildGroups(IEnumerable<GeneratedSourceItem> items)
    {
        return items
            .GroupBy(item => NormalizeGroupKey(item.Type))
            .Select(group => new GroupBucket(
                group.Key,
                GroupLabel(group.Key),
                group
                    .OrderBy(item => DisplayLabel(item), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.NormalizedLabel, StringComparer.OrdinalIgnoreCase)
                    .ToList()))
            .OrderBy(group => GroupRank(group.Key))
            .ThenBy(group => group.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool MatchesSearch(GeneratedSourceItem item, string query)
    {
        var haystack = string.Join(' ', [
            DisplayLabel(item),
            item.Notes ?? "",
            item.NormalizedLabel,
            item.Raw,
            item.DateText ?? "",
            item.Type,
            string.Join(' ', item.Occurrences.Select(occurrence => occurrence.ContextLabel))
        ]);

        return NormalizeForComparison(haystack).Contains(query, StringComparison.Ordinal);
    }

    private static string DisplayLabel(GeneratedSourceItem item)
    {
        return item.NormalizedLabel ?? item.Raw ?? "";
    }

    private static string NormalizeGroupKey(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToLowerInvariant();
    }

    private static int GroupRank(string key)
    {
        var index = Array.IndexOf(GroupOrder, key);
        return index >= 0 ? index : GroupOrder.Length;
    }

    private static string GroupLabel(string key)
    {
        return key switch
        {
            "government" => "Government and Parliamentary",
            "legal" => "Court Filings and Legal Documents",
            "journalism" => "Journalism and Secondary Reporting",
            "social" => "Public Posts and Social Media",
            "general" => "General References",
            _ => "Other References"
        };
    }

    private static string NormalizeForComparison(string value)
    {
        return value
            .Replace("*", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .Replace("`", "", StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();
    }

    private sealed record GroupBucket(string Key, string Label, List<GeneratedSourceItem> Items);
}
