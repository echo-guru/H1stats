namespace H1Stats.Core.Configuration;

/// <summary>
/// Local research LLM (llama.cpp OpenAI-compatible server). The API key stays on the H1Stats
/// server — set <c>RESEARCH_LLM_API_KEY</c> or <c>ResearchLlm:ApiKey</c> via environment /
/// user secrets / gitignored appsettings. Never send the key to the browser.
/// </summary>
public class ResearchLlmSettings
{
    public const string SectionName = "ResearchLlm";

    public const string EnvBaseUrl = "RESEARCH_LLM_BASE_URL";
    public const string EnvApiKey = "RESEARCH_LLM_API_KEY";
    public const string EnvTimeoutSeconds = "RESEARCH_LLM_TIMEOUT_SECONDS";
    public const string EnvContextSize = "RESEARCH_LLM_CONTEXT_SIZE";

    /// <summary>llama.cpp server origin, e.g. http://192.168.12.236:8080</summary>
    public string BaseUrl { get; set; } = "http://192.168.12.236:8080";

    /// <summary>Bearer token for the local LLM. Empty until configured on the server.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>HTTP timeout for inference requests.</summary>
    public int TimeoutSeconds { get; set; } = 900;

    /// <summary>Configured context window (tokens). Used for future screening prompts.</summary>
    public int ContextSize { get; set; } = 8192;

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    public string NormalizedBaseUrl => (BaseUrl ?? "").Trim().TrimEnd('/');

    /// <summary>
    /// Overlay <c>RESEARCH_LLM_*</c> environment variables on top of appsettings binding.
    /// Environment values win so the key never needs to live in committed JSON.
    /// </summary>
    public static void ApplyEnvironmentOverrides(ResearchLlmSettings settings)
    {
        var url = Environment.GetEnvironmentVariable(EnvBaseUrl);
        if (!string.IsNullOrWhiteSpace(url))
            settings.BaseUrl = url.Trim();

        var key = Environment.GetEnvironmentVariable(EnvApiKey);
        if (!string.IsNullOrWhiteSpace(key))
            settings.ApiKey = key.Trim();

        if (int.TryParse(Environment.GetEnvironmentVariable(EnvTimeoutSeconds), out var timeout) && timeout > 0)
            settings.TimeoutSeconds = timeout;

        if (int.TryParse(Environment.GetEnvironmentVariable(EnvContextSize), out var context) && context > 0)
            settings.ContextSize = context;
    }
}
