using System.Text.Json.Serialization;

namespace DossierApi.Models;

public class Section
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string DocType { get; set; } = "";
    public string DocVersion { get; set; } = "";
    public int SortOrder { get; set; }
    public string TagsPresent { get; set; } = "[]"; // JSON array
    [JsonIgnore]
    public string SourceHeading { get; set; } = "";
}

public class SectionSummary
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string DocType { get; set; } = "";
    public string DocVersion { get; set; } = "";
    public int SortOrder { get; set; }
    public string TagsPresent { get; set; } = "[]";
}
