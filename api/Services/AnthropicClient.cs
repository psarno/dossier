using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DossierApi.Models;

namespace DossierApi.Services;

public class AnthropicClient
    : IAiClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly ILogger<AnthropicClient> _logger;
    private readonly PipelineLog _pipelineLog;
    private readonly ResearchConfig _researchConfig;
    private readonly string[] _tagKeys;

    public string ProviderName => "anthropic";
    public string ModelName => _model;

    public AnthropicClient(IConfiguration config, IHttpClientFactory httpFactory, ILogger<AnthropicClient> logger, PipelineLog pipelineLog, ResearchConfig researchConfig)
    {
        _http = httpFactory.CreateClient("anthropic");
        _apiKey = config["ANTHROPIC_API_KEY"] ?? "";
        _model = config["AI_MODEL"] ?? "claude-haiku-4-5-20251001";
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

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogError("ANTHROPIC_API_KEY is not set — cannot run AI extraction");
            return null;
        }

        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var result = await CallApi(prompt, ct);
                if (result != null) return result;
                _logger.LogWarning("AI extraction attempt {Attempt} returned null for section: {Title}", attempt + 1, title);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("AI extraction attempt {Attempt} failed for section {Title}: {Message}", attempt + 1, title, ex.Message);
                _pipelineLog.Warn($"AI extraction attempt {attempt + 1} failed for '{title}': {ex.Message}");
            }
        }
        return null;
    }

    private async Task<AiExtraction?> CallApi(string prompt, CancellationToken ct)
    {
        var requestBody = new
        {
            model = _model,
            max_tokens = 300,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            var msg = $"Anthropic API error {(int)response.StatusCode}: {errorBody[..Math.Min(300, errorBody.Length)]}";
            _logger.LogError(msg);
            _pipelineLog.Error(msg);
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        var content = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "";

        return ParseSectionExtraction(content, _tagKeys);
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

        if (string.IsNullOrWhiteSpace(_apiKey)) return null;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var result = await CallGraphApi(prompt, ct);
                if (result != null) return result;
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

        if (string.IsNullOrWhiteSpace(_apiKey)) return [];

        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var result = await CallBridgeApi(prompt, ct);
                if (result != null) return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Graph bridge extraction attempt {Attempt} failed: {Message}", attempt + 1, ex.Message);
                _pipelineLog.Warn($"Graph bridge extraction attempt {attempt + 1} failed: {ex.Message}");
            }
        }

        return [];
    }

    private async Task<GraphData?> CallGraphApi(string prompt, CancellationToken ct)
    {
        var requestBody = new
        {
            model = _model,
            max_tokens = 8192,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            var msg = $"Anthropic API error {(int)response.StatusCode}: {errorBody[..Math.Min(300, errorBody.Length)]}";
            _logger.LogError(msg);
            _pipelineLog.Error(msg);
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        var content = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "";

        return ParseGraphExtraction(content, _pipelineLog);
    }

    private async Task<List<GraphEdge>?> CallBridgeApi(string prompt, CancellationToken ct)
    {
        var requestBody = new
        {
            model = _model,
            max_tokens = 1000,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            var msg = $"Anthropic API error {(int)response.StatusCode}: {errorBody[..Math.Min(300, errorBody.Length)]}";
            _logger.LogError(msg);
            _pipelineLog.Error(msg);
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        var content = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "";

        return ParseGraphBridgeExtraction(content, _pipelineLog);
    }

    internal static AiExtraction? ParseSectionExtraction(string content, string[] validTags)
    {
        content = PrepareJsonContent(content);

        var extraction = JsonSerializer.Deserialize<AiExtraction>(content, JsonOptions);
        if (extraction == null) return null;

        if (string.IsNullOrWhiteSpace(extraction.Slug))
            extraction.Slug = MarkdownParser.MakeSlug(extraction.Title ?? "section");

        extraction.TagsPresent = extraction.TagsPresent
            .Where(t => validTags.Contains(t))
            .Distinct()
            .ToList();

        return extraction;
    }

    internal static GraphData? ParseGraphExtraction(string content, PipelineLog pipelineLog)
    {
        content = PrepareJsonContent(content);

        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        content = content[start..(end + 1)];

        try
        {
            return JsonSerializer.Deserialize<GraphData>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            var preview = content[..Math.Min(200, content.Length)];
            pipelineLog.Warn($"Graph JSON parse failed: {ex.Message} — content preview: {preview}");
            return null;
        }
    }

    internal static List<GraphEdge>? ParseGraphBridgeExtraction(string content, PipelineLog pipelineLog)
    {
        content = PrepareJsonContent(content);

        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        content = content[start..(end + 1)];

        try
        {
            var result = JsonSerializer.Deserialize<GraphBridgeExtraction>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return result?.Edges ?? [];
        }
        catch (Exception ex)
        {
            var preview = content[..Math.Min(200, content.Length)];
            pipelineLog.Warn($"Graph bridge JSON parse failed: {ex.Message} — content preview: {preview}");
            return null;
        }
    }

    private static string PrepareJsonContent(string content)
    {
        content = content.Trim();
        if (content.StartsWith("```"))
            content = StripFences(content);
        return content;
    }

    private static string StripFences(string s)
    {
        var m = System.Text.RegularExpressions.Regex.Match(s, @"```(?:json)?\s*([\s\S]*?)```");
        return m.Success ? m.Groups[1].Value.Trim() : s;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
}

public class AiExtraction
{
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = "";

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("tags_present")]
    public List<string> TagsPresent { get; set; } = [];
}

public class GraphBridgeExtraction
{
    [JsonPropertyName("edges")]
    public List<GraphEdge> Edges { get; set; } = [];
}
