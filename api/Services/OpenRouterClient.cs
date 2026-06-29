using System.Text;
using System.Text.Json;
using DossierApi.Models;

namespace DossierApi.Services;

public class OpenRouterClient : IAiClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly ILogger<OpenRouterClient> _logger;
    private readonly PipelineLog _pipelineLog;
    private readonly ResearchConfig _researchConfig;
    private readonly string[] _tagKeys;

    public string ProviderName => "openrouter";
    public string ModelName => _model;

    public OpenRouterClient(IConfiguration config, IHttpClientFactory httpFactory, ILogger<OpenRouterClient> logger, PipelineLog pipelineLog, ResearchConfig researchConfig)
    {
        _http = httpFactory.CreateClient("openrouter");
        _apiKey = config["OPENROUTER_API_KEY"] ?? "";
        _model = config["AI_MODEL"] ?? "";
        _logger = logger;
        _pipelineLog = pipelineLog;
        _researchConfig = researchConfig;
        _tagKeys = researchConfig.Tags.Select(t => t.Key).ToArray();
    }

    public async Task<AiExtraction?> ExtractSectionMetadata(string title, string body, CancellationToken ct = default)
    {
        var truncatedBody = body[..Math.Min(800, body.Length)];
        var validTagLine = string.Join(", ", _tagKeys.Select(t => $"\"{t}\""));
        var prompt = $$"""
            You are processing a section from a public-interest research document about {{_researchConfig.Subject}}.

            Section title (raw): {{title}}
            Section body (first 800 chars): {{truncatedBody}}

            Return ONLY valid JSON matching this exact schema — no markdown, no explanation:
            {
              "slug": "<url-safe lowercase hyphenated identifier, max 80 chars, derived from title>",
              "title": "<clean display title, no leading Roman numerals, no markdown>",
              "tags_present": ["<tag>", ...]
            }

            Valid tag values (only use these exact strings):
            {{validTagLine}}

            Only include tags that actually appear in the body text as [TAG] markers.
            """;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var content = await CallApi(prompt, 300, ct);
                if (content == null)
                {
                    _logger.LogWarning("AI extraction attempt {Attempt} returned null for section: {Title}", attempt + 1, title);
                    continue;
                }

                return AnthropicClient.ParseSectionExtraction(content, _tagKeys);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("AI extraction attempt {Attempt} failed for section {Title}: {Message}", attempt + 1, title, ex.Message);
                _pipelineLog.Warn($"AI extraction attempt {attempt + 1} failed for '{title}': {ex.Message}");
            }
        }

        return null;
    }

    public async Task<GraphData?> ExtractGraphData(string chunkText, CancellationToken ct = default)
    {
        var truncated = chunkText.Length > 8000 ? chunkText[..8000] : chunkText;
        var prompt = $$"""
            Extract a relationship graph from this {{_researchConfig.Subject}} research text.
            Return ONLY valid JSON — no markdown fences, no preamble:
            {
              "nodes": [{"id":"url-safe-id","name":"Display Name","type":"person|institution|location|event|fund","tier":1,"tags":["CONFIRMED"],"notes":"≤10 words"}],
              "edges": [{"source":"id","target":"id","relationship":"≤5 words","evidentiary_weight":"CONFIRMED|CORROBORATED|DOCUMENTED_CLAIM|CONFIRMED_GOVT_ACTION|ANOMALOUS","directional":false}]
            }
            Rules:
            - notes: 10 words MAX — be extremely brief
            - relationship: 5 words MAX
            - tier 1=significant role, tier 2=named only
            - IDs: lowercase-hyphenated from name
            - Only include entities explicitly in the text

            Text:
            {{truncated}}
            """;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var content = await CallApi(prompt, 8192, ct);
                if (content == null) continue;
                return AnthropicClient.ParseGraphExtraction(content, _pipelineLog);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Graph extraction attempt {Attempt} failed: {Message}", attempt + 1, ex.Message);
                _pipelineLog.Warn($"Graph extraction attempt {attempt + 1} failed: {ex.Message}");
            }
        }

        return null;
    }

    public async Task<List<GraphEdge>> ExtractGraphBridgeData(string chunkText, IReadOnlyCollection<string> islandNodeNames, IReadOnlyCollection<string> mainNodeNames, CancellationToken ct = default)
    {
        var truncated = chunkText.Length > 8000 ? chunkText[..8000] : chunkText;
        var islandList = string.Join(", ", islandNodeNames);
        var mainList = string.Join(", ", mainNodeNames);
        var prompt = $$"""
            Find ONLY explicit bridge edges in this {{_researchConfig.Subject}} research text.
            A bridge edge must connect:
            - one node from this disconnected island: {{islandList}}
            - to one node from this existing main graph: {{mainList}}

            Return ONLY valid JSON — no markdown fences, no preamble:
            {
              "edges": [{"source":"Display Name","target":"Display Name","relationship":"≤5 words","evidentiary_weight":"CONFIRMED|CORROBORATED|DOCUMENTED_CLAIM|CONFIRMED_GOVT_ACTION|ANOMALOUS","directional":false}]
            }

            Rules:
            - Only return edges explicitly stated in the text
            - Do not infer missing links
            - Only return edges where one endpoint is from the disconnected island list and the other endpoint is from the existing main graph list
            - If no explicit bridge exists, return {"edges":[]}
            - relationship: 5 words MAX

            Text:
            {{truncated}}
            """;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var content = await CallApi(prompt, 1000, ct);
                if (content == null) continue;
                return AnthropicClient.ParseGraphBridgeExtraction(content, _pipelineLog) ?? [];
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Graph bridge extraction attempt {Attempt} failed: {Message}", attempt + 1, ex.Message);
                _pipelineLog.Warn($"Graph bridge extraction attempt {attempt + 1} failed: {ex.Message}");
            }
        }

        return [];
    }

    private async Task<string?> CallApi(string prompt, int maxTokens, CancellationToken ct)
    {
        var requestBody = new
        {
            model = _model,
            max_tokens = maxTokens,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
        request.Headers.Add("Authorization", $"Bearer {_apiKey}");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            var msg = $"OpenRouter API error {(int)response.StatusCode}: {errorBody[..Math.Min(300, errorBody.Length)]}";
            _logger.LogError(msg);
            _pipelineLog.Error(msg);
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        var message = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message");

        if (message.TryGetProperty("content", out var content))
        {
            if (content.ValueKind == JsonValueKind.String)
                return content.GetString();

            if (content.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var item in content.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        parts.Add(item.GetString() ?? "");
                        continue;
                    }

                    if (item.ValueKind == JsonValueKind.Object &&
                        item.TryGetProperty("text", out var textProp))
                    {
                        parts.Add(textProp.GetString() ?? "");
                    }
                }
                return string.Concat(parts);
            }
        }

        return null;
    }
}
