using NVS.Core.Interfaces;
using NVS.Core.LLM;
using ExplorerLlm = SQLiteExplorer.Lib.Services;

namespace NVS.Helpers;

/// <summary>
/// Adapts the NVS LLM service to the SQLiteExplorer library's minimal LLM interface,
/// so the embedded Database Explorer shares the endpoint and model configured in
/// NVS Settings instead of maintaining its own.
/// </summary>
public sealed class SqlExplorerLlmAdapter : ExplorerLlm.ILlmService
{
    private readonly ILlmService _inner;

    public SqlExplorerLlmAdapter(ILlmService inner)
    {
        _inner = inner;
    }

    public bool IsConfigured => _inner.IsConfigured;

    public async Task<string> ChatAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var response = await _inner.SendAsync(
            BuildRequest(systemPrompt, userPrompt, stream: false),
            cancellationToken: ct);

        return response.Content;
    }

    public async Task<string> ChatStreamingAsync(
        string systemPrompt,
        string userPrompt,
        Action<string>? onToken,
        CancellationToken ct = default)
    {
        var response = await _inner.SendAsync(
            BuildRequest(systemPrompt, userPrompt, stream: true),
            onToken: onToken,
            cancellationToken: ct);

        return response.Content;
    }

    private static ChatCompletionRequest BuildRequest(string systemPrompt, string userPrompt, bool stream) => new()
    {
        Model = string.Empty, // resolved from NVS settings by LlmService
        Messages =
        [
            ChatCompletionMessage.System(systemPrompt),
            ChatCompletionMessage.User(userPrompt)
        ],
        Stream = stream
    };
}
