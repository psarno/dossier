using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DossierApi.Models;

namespace DossierApi.Services;

public class GraphExtractService
{
    private readonly IAiClient _ai;
    private readonly IConfiguration _config;
    private readonly ILogger<GraphExtractService> _logger;
    private readonly PipelineLog _pipelineLog;
    private readonly Dictionary<string, string> _aliasMap;

    private static readonly HashSet<string> SummaryBoilerplateTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "A Factual Summary Based on DOJ Released Documents",
        "What This Document Is",
        "Tagging Key",
        "Sources",
        "General Source Categories",
    };

    private static readonly HashSet<string> NamesBoilerplateTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Person-Indexed Cross-Reference & Research Tracker",
        "How to Use This Document",
        "TIER 1: DOCUMENTED FIGURES",
        "TIER 2: RESEARCH QUEUE",
    };

    private const int BridgeRecoveryMaxComponentSize = 5;
    private const int BridgeRecoveryMaxMainCandidates = 12;

    public GraphExtractService(
        IAiClient ai,
        IConfiguration config,
        ILogger<GraphExtractService> logger,
        PipelineLog pipelineLog,
        ResearchConfig researchConfig)
    {
        _ai = ai;
        _config = config;
        _logger = logger;
        _pipelineLog = pipelineLog;
        _aliasMap = researchConfig.CentralNode.Aliases
            .ToDictionary(
                alias => alias,
                _ => researchConfig.CentralNode.Id,
                StringComparer.OrdinalIgnoreCase);
    }

    public async Task ExtractAndSaveAsync(string? summaryContent, string? namesContent, CancellationToken ct)
    {
        var chunks = BuildChunks(summaryContent, namesContent);
        _pipelineLog.Info($"Graph extraction: {chunks.Count} chunks to process");

        int completed = 0;
        int total = chunks.Count;
        var semaphore = new SemaphoreSlim(3, 3);
        var results = new System.Collections.Concurrent.ConcurrentBag<ChunkExtraction>();

        var tasks = chunks.Select(async chunk =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var data = await _ai.ExtractGraphData(chunk, ct);
                int n = Interlocked.Increment(ref completed);
                if (data != null)
                {
                    results.Add(new ChunkExtraction(chunk, data));
                    _pipelineLog.Info($"Graph chunk [{n}/{total}]: {data.Nodes.Count} nodes, {data.Edges.Count} edges");
                }
                else
                {
                    _pipelineLog.Warn($"Graph chunk [{n}/{total}]: extraction returned null");
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        var extractedChunks = results.ToList();
        var merged = MergeChunks([.. extractedChunks.Select(r => r.Data)]);
        _pipelineLog.Info($"Graph merged: {merged.Nodes.Count} nodes, {merged.Edges.Count} edges");

        var recoveredEdges = await RecoverBridgeEdgesAsync(extractedChunks, merged, ct);
        if (recoveredEdges.Count > 0)
        {
            AddEdgesToGraph(merged, recoveredEdges);
            _pipelineLog.Info($"Graph bridge recovery added {recoveredEdges.Count} edges");
            _pipelineLog.Info($"Graph post-recovery: {merged.Nodes.Count} nodes, {merged.Edges.Count} edges");
        }

        string dbPath = _config["DB_PATH"] ?? "/data/dossier.db";
        string graphPath = Path.Combine(Path.GetDirectoryName(dbPath)!, "graph.json");

        var graphJson = JsonSerializer.Serialize(merged, new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        await File.WriteAllTextAsync(graphPath, graphJson, ct);
        _pipelineLog.Info($"Graph saved to {graphPath}");
    }

    private static List<string> BuildChunks(string? summaryContent, string? namesContent)
    {
        var segments = new List<string>();

        if (summaryContent != null)
        {
            var parts = summaryContent.Split("\n## ", StringSplitOptions.RemoveEmptyEntries);
            segments.AddRange(parts
                .Skip(1) // Drop document preamble before the first semantic ## heading
                .Select(p => "## " + p.TrimStart('#', ' '))
                .Where(s => !IsBoilerplateSegment(s, SummaryBoilerplateTitles)));
        }

        if (namesContent != null)
        {
            var parts = namesContent.Split("\n### ", StringSplitOptions.RemoveEmptyEntries);
            segments.AddRange(parts
                .Skip(1) // Drop document title/introduction block before the first person entry
                .Select(p => "### " + p.TrimStart('#', ' '))
                .Where(s => !IsBoilerplateSegment(s, NamesBoilerplateTitles)));
        }

        var chunks = new List<string>();
        var current = new StringBuilder();

        foreach (var segment in segments)
        {
            var seg = segment.Length > 7000 ? segment[..7000] : segment;

            if (current.Length > 0 && current.Length + seg.Length > 5000)
            {
                chunks.Add(current.ToString());
                current.Clear();
            }

            if (seg.Length >= 5000)
            {
                if (current.Length > 0)
                {
                    chunks.Add(current.ToString());
                    current.Clear();
                }
                chunks.Add(seg);
            }
            else
            {
                current.Append(seg).Append('\n');
            }
        }

        if (current.Length > 0)
            chunks.Add(current.ToString());

        return chunks;
    }

    private async Task<List<GraphEdge>> RecoverBridgeEdgesAsync(List<ChunkExtraction> chunkExtractions, GraphData merged, CancellationToken ct)
    {
        var components = GetConnectedComponents(merged);
        if (components.Count <= 1) return [];

        var mainComponent = components
            .OrderByDescending(c => c.Count)
            .First();

        var nodeById = merged.Nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        var mainNodeIds = new HashSet<string>(mainComponent, StringComparer.OrdinalIgnoreCase);
        var recovered = new List<GraphEdge>();
        var recoveredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var component in components.Where(c => !c.SetEquals(mainComponent) && c.Count <= BridgeRecoveryMaxComponentSize))
        {
            var islandNodes = component
                .Where(nodeById.ContainsKey)
                .Select(id => nodeById[id])
                .ToList();

            var islandNodeNames = islandNodes
                .Select(n => n.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var candidateChunks = chunkExtractions
                .Where(c => ChunkTouchesComponent(c.Data, component))
                .ToList();

            _pipelineLog.Info($"Graph bridge recovery: component size {component.Count}, candidate chunks {candidateChunks.Count}");

            foreach (var chunk in candidateChunks)
            {
                var mainCandidates = FindMainGraphMentions(chunk.Text, merged.Nodes, mainNodeIds);
                if (mainCandidates.Count == 0) continue;

                var bridgeEdges = await _ai.ExtractGraphBridgeData(chunk.Text, islandNodeNames, mainCandidates, ct);
                foreach (var edge in bridgeEdges)
                {
                    if (!TryNormalizeBridgeEdge(edge, component, mainNodeIds, out var normalized)) continue;

                    var key = BuildEdgeKey(normalized.Source, normalized.Target, normalized.Relationship);
                    if (recoveredKeys.Add(key))
                        recovered.Add(normalized);
                }
            }
        }

        return recovered;
    }

    private bool ChunkTouchesComponent(GraphData data, HashSet<string> component)
    {
        foreach (var node in data.Nodes)
        {
            if (component.Contains(NormalizeId(node.Name)))
                return true;
        }

        foreach (var edge in data.Edges)
        {
            if (component.Contains(NormalizeId(edge.Source)) || component.Contains(NormalizeId(edge.Target)))
                return true;
        }

        return false;
    }

    private List<string> FindMainGraphMentions(string chunkText, List<GraphNode> nodes, HashSet<string> mainNodeIds)
    {
        var matches = new List<(string Name, int Score)>();

        foreach (var node in nodes)
        {
            if (!mainNodeIds.Contains(node.Id)) continue;

            var score = CountMentionScore(chunkText, node);
            if (score > 0)
                matches.Add((node.Name, score));
        }

        return matches
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.Name.Length)
            .Select(m => m.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(BridgeRecoveryMaxMainCandidates)
            .ToList();
    }

    private int CountMentionScore(string chunkText, GraphNode node)
    {
        int score = 0;
        foreach (var variant in GetNameVariants(node.Name))
        {
            if (string.IsNullOrWhiteSpace(variant)) continue;
            if (chunkText.Contains(variant, StringComparison.OrdinalIgnoreCase))
                score += variant.Length;
        }
        return score;
    }

    private IEnumerable<string> GetNameVariants(string name)
    {
        yield return name;

        var canonicalId = NormalizeId(name);
        foreach (var alias in _aliasMap.Where(kvp => kvp.Value.Equals(canonicalId, StringComparison.OrdinalIgnoreCase))
                     .Select(kvp => kvp.Key))
        {
            yield return alias;
        }
    }

    private bool TryNormalizeBridgeEdge(GraphEdge edge, HashSet<string> islandIds, HashSet<string> mainNodeIds, out GraphEdge normalized)
    {
        normalized = edge;

        if (string.IsNullOrWhiteSpace(edge.Source) || string.IsNullOrWhiteSpace(edge.Target))
            return false;

        var srcId = NormalizeId(edge.Source);
        var tgtId = NormalizeId(edge.Target);

        bool srcIsland = islandIds.Contains(srcId);
        bool tgtIsland = islandIds.Contains(tgtId);
        bool srcMain = mainNodeIds.Contains(srcId);
        bool tgtMain = mainNodeIds.Contains(tgtId);

        if (!((srcIsland && tgtMain) || (tgtIsland && srcMain)))
            return false;

        normalized = new GraphEdge
        {
            Source = srcId,
            Target = tgtId,
            Relationship = string.IsNullOrWhiteSpace(edge.Relationship) ? "related" : edge.Relationship.ToLowerInvariant(),
            EvidentiaryWeight = edge.EvidentiaryWeight,
            Directional = edge.Directional
        };

        return true;
    }

    private static void AddEdgesToGraph(GraphData graph, List<GraphEdge> edges)
    {
        var nodeIds = graph.Nodes
            .Select(n => n.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var edgeSet = graph.Edges
            .Select(e => BuildEdgeKey(e.Source, e.Target, e.Relationship))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var edge in edges)
        {
            if (!nodeIds.Contains(edge.Source) || !nodeIds.Contains(edge.Target))
                continue;

            var key = BuildEdgeKey(edge.Source, edge.Target, edge.Relationship);
            if (edgeSet.Add(key))
                graph.Edges.Add(edge);
        }
    }

    private static string BuildEdgeKey(string source, string target, string relationship)
    {
        var rel = string.IsNullOrWhiteSpace(relationship) ? "related" : relationship.ToLowerInvariant();
        return $"{source}::{target}::{rel}";
    }

    private static List<HashSet<string>> GetConnectedComponents(GraphData graph)
    {
        var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in graph.Nodes)
            adjacency[node.Id] = [];

        foreach (var edge in graph.Edges)
        {
            if (!adjacency.ContainsKey(edge.Source)) adjacency[edge.Source] = [];
            if (!adjacency.ContainsKey(edge.Target)) adjacency[edge.Target] = [];
            adjacency[edge.Source].Add(edge.Target);
            adjacency[edge.Target].Add(edge.Source);
        }

        var components = new List<HashSet<string>>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var nodeId in adjacency.Keys)
        {
            if (!seen.Add(nodeId)) continue;

            var component = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            queue.Enqueue(nodeId);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                component.Add(current);

                foreach (var next in adjacency[current])
                {
                    if (seen.Add(next))
                        queue.Enqueue(next);
                }
            }

            components.Add(component);
        }

        return components;
    }

    private static bool IsBoilerplateSegment(string segment, HashSet<string> excludedTitles)
    {
        var firstLine = segment
            .Split('\n', 2)[0]
            .Trim();

        var title = firstLine.TrimStart('#', ' ', '\t').Trim();
        return excludedTitles.Contains(title);
    }

    private string NormalizeId(string name)
    {
        name = name.Trim();
        if (_aliasMap.TryGetValue(name, out var alias))
            return alias;
        return Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
    }

    private GraphData MergeChunks(List<GraphData> chunks)
    {
        var nodeMap = new Dictionary<string, GraphNode>(StringComparer.OrdinalIgnoreCase);
        var edgeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var edges = new List<GraphEdge>();

        foreach (var chunk in chunks)
        {
            foreach (var node in chunk.Nodes)
            {
                if (string.IsNullOrWhiteSpace(node.Name)) continue;
                var id = NormalizeId(node.Name);
                node.Id = id;

                if (!nodeMap.TryGetValue(id, out var existing))
                {
                    nodeMap[id] = node;
                }
                else
                {
                    existing.Tags = [.. existing.Tags.Union(node.Tags).Distinct()];
                    if (string.IsNullOrWhiteSpace(existing.Notes) && !string.IsNullOrWhiteSpace(node.Notes))
                        existing.Notes = node.Notes;
                }
            }
        }

        foreach (var chunk in chunks)
        {
            foreach (var edge in chunk.Edges)
            {
                if (string.IsNullOrWhiteSpace(edge.Source) || string.IsNullOrWhiteSpace(edge.Target)) continue;

                var srcId = NormalizeId(edge.Source);
                var tgtId = NormalizeId(edge.Target);

                if (!nodeMap.ContainsKey(srcId) || !nodeMap.ContainsKey(tgtId)) continue;

                edge.Source = srcId;
                edge.Target = tgtId;

                var rel = string.IsNullOrWhiteSpace(edge.Relationship) ? "related" : edge.Relationship.ToLowerInvariant();
                var key = BuildEdgeKey(srcId, tgtId, rel);
                if (edgeSet.Add(key))
                    edges.Add(edge);
            }
        }

        var connectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in edges)
        {
            connectedIds.Add(edge.Source);
            connectedIds.Add(edge.Target);
        }

        return new GraphData
        {
            Nodes = [.. nodeMap.Values.Where(n => connectedIds.Contains(n.Id))],
            Edges = edges,
        };
    }

    private sealed record ChunkExtraction(string Text, GraphData Data);
}
