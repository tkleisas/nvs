namespace NVS.Core.Models.Settings;

public sealed record LlmSettings
{
    /// <summary>LLM API endpoint URL (e.g., "http://127.0.0.1:1234", "https://openrouter.ai/api").</summary>
    public string Endpoint { get; init; } = "http://127.0.0.1:1234";

    /// <summary>API key / auth token. Empty for local models.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Auth scheme (e.g., "Bearer").</summary>
    public string AuthScheme { get; init; } = "Bearer";

    /// <summary>Model ID sent in API requests (e.g., "deepseek-chat", "qwen/qwen3-coder-30b").</summary>
    public string Model { get; init; } = "codellama";

    /// <summary>API completions path (e.g., "v1/chat/completions").</summary>
    public string CompletionsPath { get; init; } = "v1/chat/completions";

    /// <summary>Max output tokens per response.</summary>
    public int MaxTokens { get; init; } = 4096;

    /// <summary>Max context window size in tokens.</summary>
    public int MaxContextLength { get; init; } = 32_000;

    /// <summary>Sampling temperature (0.0–2.0).</summary>
    public double Temperature { get; init; } = 0.2;

    /// <summary>Enable streaming responses.</summary>
    public bool Stream { get; init; } = true;

    /// <summary>Max tool-calling iterations per agent loop.</summary>
    public int MaxIterations { get; init; } = 20;

    /// <summary>HTTP request timeout in seconds.</summary>
    public int HttpTimeoutSeconds { get; init; } = 120;

    /// <summary>Enable the LLM chat panel.</summary>
    public bool EnableChat { get; init; } = true;

    /// <summary>Enable LLM-powered auto-complete suggestions.</summary>
    public bool EnableAutoComplete { get; init; } = false;

    /// <summary>Active system prompt template name (coding, debugging, testing, general).</summary>
    public string ActivePromptTemplate { get; init; } = "general";
}
