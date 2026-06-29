namespace DossierApi.Models;

public class PipelineResult
{
    public bool Success { get; set; }
    public int SectionCount { get; set; }
    public int EntryCount { get; set; }
    public double ElapsedSeconds { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public string? SummaryVersion { get; set; }
    public string? NamesVersion { get; set; }
    public string? FrameworkVersion { get; set; }
}

public class PipelineStatus
{
    public bool IsRunning { get; set; }
    public DateTime? LastRunAt { get; set; }
    public PipelineResult? LastResult { get; set; }
    public string? CurrentSummaryVersion { get; set; }
    public string? CurrentNamesVersion { get; set; }
    public string? CurrentFrameworkVersion { get; set; }
    public string? BuiltAt { get; set; }
}
