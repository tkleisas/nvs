using NVS.Core.Interfaces;
using NVS.Core.LLM;
using ExplorerLlm = ApiClient.Core.Llm;

namespace NVS.Helpers;

/// <summary>
/// Adapts the NVS LLM service to the ApiClient library's minimal LLM interface,
/// so the embedded API Client shares the endpoint and model configured in
/// NVS Settings instead of maintaining its own.
/// </summary>
public sealed class ApiClientLlmAdapter : ExplorerLlm.ILlmService
{
    private readonly ILlmService _inner;

    public ApiClientLlmAdapter(ILlmService inner)
    {
        _inner = inner;
    }

    public bool IsConfigured => _inner.IsConfigured;

    public async Task<string> ChatAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var response = await _inner.SendAsync(
            new ChatCompletionRequest
            {
                Model = string.Empty, // resolved from NVS settings by LlmService
                Messages =
                [
                    ChatCompletionMessage.System(systemPrompt),
                    ChatCompletionMessage.User(userPrompt)
                ],
                Stream = false
            },
            cancellationToken: ct);

        return response.Content;
    }
}
