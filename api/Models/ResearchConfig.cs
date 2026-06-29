namespace DossierApi.Models;

public class ResearchConfig
{
    public string Subject { get; set; } = "";
    public BrandingConfig Branding { get; set; } = new();
    public CentralNodeConfig CentralNode { get; set; } = new();
    public List<DocumentConfig> Documents { get; set; } = [];
    public List<TagConfig> Tags { get; set; } = [];
    public SourceCitationConfig SourceCitation { get; set; } = new();
}

public class BrandingConfig
{
    public string SiteTitle { get; set; } = "";
    public string NavBrand { get; set; } = "";
    public string Tagline { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public string Domain { get; set; } = "";
}

public class CentralNodeConfig
{
    public string Id { get; set; } = "";
    public List<string> Aliases { get; set; } = [];
}

public class DocumentConfig
{
    public string Type { get; set; } = "";
    public string Label { get; set; } = "";
    public string Route { get; set; } = "";
}

public class TagConfig
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Description { get; set; } = "";
}

public class SourceCitationConfig
{
    public bool Enabled { get; set; } = true;
    public string Label { get; set; } = "Source";
}
