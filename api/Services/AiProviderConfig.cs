namespace DossierApi.Services;

public static class AiProviderConfig
{
    public static void Validate(IConfiguration config)
    {
        var provider = config["AI_PROVIDER"]?.Trim().ToLowerInvariant();
        var model = config["AI_MODEL"]?.Trim();
        var anthropicKey = config["ANTHROPIC_API_KEY"]?.Trim();
        var openRouterKey = config["OPENROUTER_API_KEY"]?.Trim();

        if (string.IsNullOrWhiteSpace(provider))
            throw new InvalidOperationException("AI_PROVIDER is required and must be 'anthropic' or 'openrouter'.");

        if (provider is not ("anthropic" or "openrouter"))
            throw new InvalidOperationException($"Unsupported AI_PROVIDER '{provider}'. Expected 'anthropic' or 'openrouter'.");

        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("AI_MODEL is required.");

        if (!string.IsNullOrWhiteSpace(anthropicKey) && !string.IsNullOrWhiteSpace(openRouterKey))
            throw new InvalidOperationException("Configure exactly one AI provider key. Do not set both ANTHROPIC_API_KEY and OPENROUTER_API_KEY.");

        if (provider == "anthropic" && string.IsNullOrWhiteSpace(anthropicKey))
            throw new InvalidOperationException("AI_PROVIDER=anthropic requires ANTHROPIC_API_KEY.");

        if (provider == "openrouter" && string.IsNullOrWhiteSpace(openRouterKey))
            throw new InvalidOperationException("AI_PROVIDER=openrouter requires OPENROUTER_API_KEY.");
    }
}
