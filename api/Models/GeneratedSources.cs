namespace DossierApi.Models;

public class GeneratedSourcesReport
{
    public string GeneratedAt { get; set; } = "";
    public string SummaryVersion { get; set; } = "";
    public string NamesVersion { get; set; } = "";
    public SourceTotals Totals { get; set; } = new();
    public List<GeneratedSourceItem> Items { get; set; } = [];
    public List<SourceReviewItem> Review { get; set; } = [];
}

public class GeneratedSourcesSummaryResponse
{
    public string GeneratedAt { get; set; } = "";
    public string SummaryVersion { get; set; } = "";
    public string NamesVersion { get; set; } = "";
    public SourceTotals Totals { get; set; } = new();
    public List<GeneratedSourcesGroupSummary> Groups { get; set; } = [];
}

public class GeneratedSourcesGroupSummary
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int ItemCount { get; set; }
}

public class GeneratedSourcesGroupResponse : GeneratedSourcesGroupSummary
{
    public List<GeneratedSourceItem> Items { get; set; } = [];
}

public class SourceTotals
{
    public int TotalFragments { get; set; }
    public int ParsedItems { get; set; }
    public int AmbiguousItems { get; set; }
}

public class GeneratedSourceItem
{
    public string Raw { get; set; } = "";
    public List<string> RawVariants { get; set; } = [];
    public string NormalizedLabel { get; set; } = "";
    public string Type { get; set; } = "unknown";
    public string? Author { get; set; }
    public string? Outlet { get; set; }
    public string? Account { get; set; }
    public string? Platform { get; set; }
    public string? DateText { get; set; }
    public string? Notes { get; set; }
    public List<string> SourceDocuments { get; set; } = [];
    public List<SourceOccurrence> Occurrences { get; set; } = [];
    public string Confidence { get; set; } = "low";
    public bool NeedsReview { get; set; }
}

public class SourceOccurrence
{
    public string Document { get; set; } = "";
    public int LineNumber { get; set; }
    public string ContextLabel { get; set; } = "";
}

public class SourceReviewItem
{
    public string Raw { get; set; } = "";
    public string Document { get; set; } = "";
    public int LineNumber { get; set; }
    public string ContextLabel { get; set; } = "";
    public string Reason { get; set; } = "";
}
