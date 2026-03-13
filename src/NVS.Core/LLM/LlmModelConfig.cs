namespace NVS.Core.LLM;

/// <summary>
/// Configuration for an LLM model endpoint.
/// Supports any OpenAI-compatible provider.
/// </summary>
public sealed record LlmModelConfig
{
    /// <summary>Display name for the model (e.g., "DeepSeek Chat").</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Model ID sent in API requests (e.g., "deepseek-chat", "qwen/qwen3-coder-30b").</summary>
    public string ModelId { get; init; } = string.Empty;

    /// <summary>API host URL (e.g., "http://127.0.0.1:1234", "https://openrouter.ai/api").</summary>
    public string HostUrl { get; init; } = "http://127.0.0.1:1234";

    /// <summary>API endpoint path (e.g., "v1/chat/completions").</summary>
    public string CompletionsPath { get; init; } = "v1/chat/completions";

    /// <summary>Auth token / API key. Empty for local models that don't require auth.</summary>
    public string AuthToken { get; init; } = string.Empty;

    /// <summary>Auth scheme (e.g., "Bearer"). Empty to omit auth header.</summary>
    public string AuthScheme { get; init; } = "Bearer";

    /// <summary>Whether this is a local model (no network latency).</summary>
    public bool IsLocal { get; init; }

    /// <summary>Maximum context window size in tokens.</summary>
    public int MaxContextLength { get; init; } = 32_000;

    /// <summary>Maximum output tokens per response.</summary>
    public int MaxOutputTokens { get; init; } = 4_096;

    /// <summary>Default temperature for this model.</summary>
    public double Temperature { get; init; } = 0.2;

    /// <summary>Whether this model supports tool/function calling.</summary>
    public bool SupportsTools { get; init; } = true;

    /// <summary>Whether this model is a thinking/reasoning model (e.g., DeepSeek-R1).</summary>
    public bool IsThinkingModel { get; init; }

    /// <summary>HTTP request timeout in seconds.</summary>
    public int HttpTimeoutSeconds { get; init; } = 120;

    /// <summary>Whether this model configuration is enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Builds the full completions endpoint URL.</summary>
    public string GetCompletionsUrl()
    {
        var baseUrl = HostUrl.TrimEnd('/');
        var path = CompletionsPath.TrimStart('/');
        return $"{baseUrl}/{path}";
    }
}
