using DossierApi.Models;

namespace DossierApi.Services;

public interface IAiClient
{
    string ProviderName { get; }
    string ModelName { get; }

    Task<AiExtraction?> ExtractSectionMetadata(string title, string body, CancellationToken ct = default);
    Task<GraphData?> ExtractGraphData(string chunkText, CancellationToken ct = default);
    Task<List<GraphEdge>> ExtractGraphBridgeData(string chunkText, IReadOnlyCollection<string> islandNodeNames, IReadOnlyCollection<string> mainNodeNames, CancellationToken ct = default);
}
