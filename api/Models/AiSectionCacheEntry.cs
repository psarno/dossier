namespace DossierApi.Models;

public class AiSectionCacheEntry
{
    public string SectionHash { get; set; } = "";
    public string ProviderName { get; set; } = "";
    public string ModelName { get; set; } = "";
    public string PromptVersion { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string TagsPresent { get; set; } = "[]";
    public string CachedAt { get; set; } = "";
}
