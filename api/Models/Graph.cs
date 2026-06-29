using System.Text.Json.Serialization;

namespace DossierApi.Models;

public class GraphNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "person";

    [JsonPropertyName("tier")]
    public int Tier { get; set; } = 1;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";
}

public class GraphEdge
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("target")]
    public string Target { get; set; } = "";

    [JsonPropertyName("relationship")]
    public string Relationship { get; set; } = "";

    [JsonPropertyName("evidentiary_weight")]
    public string EvidentiaryWeight { get; set; } = "DOCUMENTED_CLAIM";

    [JsonPropertyName("directional")]
    public bool Directional { get; set; }
}

public class GraphData
{
    [JsonPropertyName("nodes")]
    public List<GraphNode> Nodes { get; set; } = [];

    [JsonPropertyName("edges")]
    public List<GraphEdge> Edges { get; set; } = [];
}
