namespace DossierApi.Models;

public class Entry
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public int Tier { get; set; }
    public string Description { get; set; } = "";
    public string SectionRefs { get; set; } = "[]"; // JSON array of section slugs
    public string DocVersion { get; set; } = "";
}

public class EntrySummary
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public int Tier { get; set; }
    public string SectionRefs { get; set; } = "[]";
}
