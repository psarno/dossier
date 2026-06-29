namespace DossierApi.Models;

public class SearchResult
{
    public string ResultType { get; set; } = ""; // "section" | "entry"
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Snippet { get; set; } = "";
    public double Rank { get; set; }
}
